#pragma warning disable SYSLIB0050

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeadlessServer
{
    /// <summary>
    /// Startup-time world selection. The world is built while static game state is being
    /// injected, which only happens once per process, so "which save to play" has to be decided
    /// before the world is constructed. This runs before the game is initialised and asks the
    /// operator to pick an existing save or start a new world.
    /// </summary>
    public static class SaveSelection
    {
        /// <summary>Keyword accepted at the prompt (and usable in config) for a brand new world.</summary>
        public const string NewWorldChoice = "new";

        /// <summary>A save the server can load: one folder with a data file, its metadata and date.</summary>
        public sealed class SaveInfo
        {
            public string FolderName { get; init; } = "";
            public string DataFile { get; init; } = "";
            public DateTime WrittenUtc { get; init; }
            public string FarmName { get; init; } = "";
            public string HostName { get; init; } = "";
            public long GameId { get; init; }
            public int Year { get; init; }
            public string Season { get; init; } = "";
            public int DayOfMonth { get; init; }
            public long SizeBytes { get; init; }

            public string Calendar => Year > 0 ? $"第{Year}年{SeasonName(Season)}{DayOfMonth}日" : "";

            private static string SeasonName(string season) => season switch
            {
                "spring" => "春",
                "summer" => "夏",
                "fall" => "秋",
                "winter" => "冬",
                _ => season
            };

            public string Describe() =>
                $"{FolderName}  {FarmName} - {HostName}  {Calendar}  {WrittenUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {SizeBytes / 1024} KB";
        }

        /// <summary>
        /// Lists the saves in the game's Saves folder, newest first.
        /// The folder and its data file share one name, as the game writes them.
        /// </summary>
        public static List<SaveInfo> ListSaves()
        {
            var saves = new List<SaveInfo>();
            try
            {
                string root = StardewValley.Program.GetSavesFolder();
                if (!Directory.Exists(root))
                    return saves;

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string folderName = Path.GetFileName(dir);
                    string dataFile = Path.Combine(dir, folderName);
                    if (!File.Exists(dataFile))
                        continue;

                    saves.Add(ReadInfo(dir, folderName, dataFile));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save] Could not list saves: {ex.Message}");
            }

            return saves.OrderByDescending(s => s.WrittenUtc).ToList();
        }

        private static SaveInfo ReadInfo(string dir, string folderName, string dataFile)
        {
            var info = new SaveInfo
            {
                FolderName = folderName,
                DataFile = dataFile,
                WrittenUtc = File.GetLastWriteTimeUtc(dataFile),
                SizeBytes = new FileInfo(dataFile).Length
            };

            // <farmName>_<gameId>; cosmetic only, the numbers are parsed from the metadata file.
            int split = folderName.LastIndexOf('_');
            if (split > 0 && long.TryParse(folderName[(split + 1)..], out long gameId))
            {
                info = new SaveInfo
                {
                    FolderName = info.FolderName,
                    DataFile = info.DataFile,
                    WrittenUtc = info.WrittenUtc,
                    SizeBytes = info.SizeBytes,
                    GameId = gameId,
                    FarmName = folderName[..split]
                };
            }

            // SaveGameInfo is a small Farmer XML the game keeps for the save list. Reading the
            // handful of values needed here avoids deserializing the whole world just to label a
            // menu entry (and the metadata file is exactly what the game itself reads).
            try
            {
                string infoPath = Path.Combine(dir, "SaveGameInfo");
                if (File.Exists(infoPath))
                {
                    var meta = ReadMetadata(infoPath);
                    return new SaveInfo
                    {
                        FolderName = info.FolderName,
                        DataFile = info.DataFile,
                        WrittenUtc = info.WrittenUtc,
                        SizeBytes = info.SizeBytes,
                        GameId = info.GameId,
                        FarmName = meta.FarmName ?? info.FarmName,
                        HostName = meta.Name ?? "",
                        Year = meta.Year,
                        Season = meta.Season ?? "",
                        DayOfMonth = meta.DayOfMonth
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][WARN] Could not read metadata for {folderName}: {ex.Message}");
            }

            return info;
        }

        private readonly record struct Metadata(string? FarmName, string? Name, int Year, string? Season, int DayOfMonth);

        private static Metadata ReadMetadata(string infoPath)
        {
            var doc = new System.Xml.XmlDocument();
            doc.Load(infoPath);
            var root = doc.DocumentElement;

            string? Text(string name) => root?.SelectSingleNode($"*[local-name()='{name}']")?.InnerText;
            int Number(string name) => int.TryParse(Text(name), out int v) ? v : 0;

            // seasonForSaveGame is a 0-based index into the season order, not a name.
            int seasonIndex = Number("seasonForSaveGame");
            string? seasonName = seasonIndex is >= 0 and < 4
                ? new[] { "spring", "summer", "fall", "winter" }[seasonIndex]
                : null;

            return new Metadata(Text("farmName"), Text("name"), Number("yearForSaveGame"), seasonName, Number("dayOfMonthForSaveGame"));
        }

        /// <summary>
        /// Decides which world to load before the world is built, using (in priority order) the
        /// <c>--save</c> command line value, the saved configuration, or an interactive prompt.
        /// The decision is written back to <see cref="ServerConfig.PathsSection.WorldSave"/>.
        /// </summary>
        public static void Resolve(ServerConfig config, bool canPrompt)
        {
            var saves = ListSaves();
            string configured = (config.Paths.WorldSave ?? "").Trim();

            // An explicit --save on the command line wins over both the file and the prompt.
            string? fromArgs = FindSaveArgument();
            if (fromArgs != null)
            {
                Apply(config, fromArgs, saves, "the --save argument");
                return;
            }

            bool hasUsableConfigured =
                configured.Equals(NewWorldChoice, StringComparison.OrdinalIgnoreCase)
                || saves.Any(s => string.Equals(s.FolderName, configured, StringComparison.OrdinalIgnoreCase));

            if (!config.Paths.AskForWorldOnStartup)
            {
                Apply(config, configured.Length == 0 ? NewWorldChoice : configured, saves, "config.json (AskForWorldOnStartup is off)");
                return;
            }

            if (!canPrompt)
            {
                Console.WriteLine("[Save] No interactive console; using the configured world.");
                Apply(config, hasUsableConfigured ? configured : NewWorldChoice, saves, "config.json (no console)");
                return;
            }

            PrintSaves(saves, configured);
            string? choice = Prompt(saves, configured);
            Apply(config, choice ?? (hasUsableConfigured ? configured : NewWorldChoice), saves, "the startup prompt");
        }

        private static void Apply(ServerConfig config, string choice, List<SaveInfo> saves, string source)
        {
            choice = (choice ?? "").Trim();

            if (choice.Equals(NewWorldChoice, StringComparison.OrdinalIgnoreCase))
            {
                config.Paths.WorldSave = NewWorldChoice;
                Console.WriteLine($"[Save] Starting a NEW world (chosen by {source}).");
            }
            else
            {
                SaveInfo? match = saves.FirstOrDefault(s => string.Equals(s.FolderName, choice, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    config.Paths.WorldSave = match.FolderName;
                    Console.WriteLine($"[Save] Loading world '{match.FolderName}' ({match.Calendar}) chosen by {source}.");
                }
                else
                {
                    // A stale or unknown folder name must not silently produce an empty farm:
                    // fall back to the newest save, and only start fresh when there is none.
                    SaveInfo? newest = saves.FirstOrDefault();
                    if (newest != null)
                    {
                        config.Paths.WorldSave = newest.FolderName;
                        Console.WriteLine($"[Save][WARN] '{choice}' is not an available save (from {source}); using the newest one instead.");
                    }
                    else
                    {
                        config.Paths.WorldSave = NewWorldChoice;
                        Console.WriteLine($"[Save][WARN] '{choice}' is not an available save (from {source}) and no saves exist; starting a new world.");
                    }
                }
            }

            ConfigLoader.Save();
        }

        private static string? FindSaveArgument()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--save", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        return args[i + 1];
                    if (args[i].StartsWith("--save=", StringComparison.OrdinalIgnoreCase))
                        return args[i]["--save=".Length..];
                }
            }
            catch
            {
                // Command line not readable in this host; fall through to the prompt.
            }
            return null;
        }

        private static void PrintSaves(List<SaveInfo> saves, string configured)
        {
            Console.WriteLine();
            Console.WriteLine("========== 选择世界 ==========");
            if (saves.Count == 0)
            {
                Console.WriteLine("  存档目录里还没有世界。");
            }
            else
            {
                Console.WriteLine($"  存档目录：{StardewValley.Program.GetSavesFolder()}");
                for (int i = 0; i < saves.Count; i++)
                {
                    string marker = string.Equals(saves[i].FolderName, configured, StringComparison.OrdinalIgnoreCase) ? "  <= 上次的选择" : "";
                    Console.WriteLine($"  [{i + 1}] {saves[i].Describe()}{marker}");
                }
            }
            Console.WriteLine($"  [n] 新建世界");
            Console.WriteLine("  直接回车 = 使用上次的选择；也可以直接输入完整的存档名。");
            Console.WriteLine("==============================");
        }

        private static string? Prompt(List<SaveInfo> saves, string configured)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Console.Write("请选择世界 > ");
                string? line;
                try
                {
                    line = Console.ReadLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Save] Console input unavailable ({ex.Message}); using the configured world.");
                    return null;
                }

                if (line == null)
                {
                    Console.WriteLine();
                    return null;
                }

                string text = line.Trim();
                if (text.Length == 0)
                    return configured.Length > 0 ? configured : saves.FirstOrDefault()?.FolderName ?? NewWorldChoice;

                if (text.Equals("n", StringComparison.OrdinalIgnoreCase)
                    || text.Equals(NewWorldChoice, StringComparison.OrdinalIgnoreCase)
                    || text == "新建"
                    || text == "0")
                    return NewWorldChoice;

                if (int.TryParse(text, out int index) && index >= 1 && index <= saves.Count)
                    return saves[index - 1].FolderName;

                SaveInfo? byName = saves.FirstOrDefault(s => string.Equals(s.FolderName, text, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                    return byName.FolderName;

                // Last resort: let the operator name a folder that is not in the list (e.g. one that
                // was just copied in); a wrong choice is reported when the world is loaded.
                if (saves.Count == 0)
                    return text;

                Console.WriteLine($"  没有这个选项：{text}（输入序号、n，或直接回车）");
            }

            Console.WriteLine("[Save] Too many invalid attempts; using the configured world.");
            return null;
        }
    }
}
