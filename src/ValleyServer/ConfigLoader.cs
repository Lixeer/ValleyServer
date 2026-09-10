#pragma warning disable SYSLIB0050

using System;
using System.IO;
using System.Text.Json;

namespace HeadlessServer
{
    /// <summary>
    /// Loads <see cref="ServerConfig"/> from <c>config.json</c> beside the executable.
    ///
    /// Behaviour:
    /// <list type="bullet">
    /// <item>A missing file is created from the built-in defaults.</item>
    /// <item>An unreadable or malformed file is reported and the defaults are used, so a
    /// typo can never stop the server from starting.</item>
    /// <item>Properties omitted from the file keep their default value.</item>
    /// <item>Out-of-range values are reported and replaced by the default.</item>
    /// </list>
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>Name of the configuration file looked up beside the executable.</summary>
        public const string FileName = "config.json";

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>Absolute path of the configuration file.</summary>
        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

        /// <summary>
        /// Reads the configuration file, applies overrides, validates the result, and
        /// publishes it through <see cref="ServerConfig.Current"/>.
        /// </summary>
        /// <param name="args">Command line arguments; these win over the file.</param>
        /// <returns>The effective configuration.</returns>
        public static ServerConfig Load(string[] args)
        {
            ServerConfig config = LoadOrCreate(ConfigPath);
            ApplyEnvironmentOverrides(config);
            ApplyCommandLineOverrides(config, args);
            Normalize(config);
            Validate(config);
            ServerConfig.Current = config;
            LogEffectiveConfig(config);
            return config;
        }

        private static ServerConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var defaults = new ServerConfig();
                try
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(defaults, WriteOptions));
                    Console.WriteLine($"[Config] Wrote default configuration to {path}");
                }
                catch (Exception ex)
                {
                    // A missing file is not fatal: the server runs on built-in defaults.
                    Console.WriteLine($"[Config] Could not write default configuration to {path}: {ex.Message}");
                }
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(path);
                ServerConfig? loaded = JsonSerializer.Deserialize<ServerConfig>(json, ReadOptions);
                if (loaded == null)
                {
                    Console.WriteLine($"[Config] {path} contained no configuration object; using defaults.");
                    return new ServerConfig();
                }
                Console.WriteLine($"[Config] Loaded configuration from {path}");
                return loaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to read {path}: {ex.Message}");
                Console.WriteLine("[Config] Using built-in defaults; fix the file and restart to apply it.");
                return new ServerConfig();
            }
        }

        /// <summary>
        /// Maps the documented environment variables onto the file's values. Environment
        /// variables win over the file so container deployments can override it without
        /// editing it.
        /// </summary>
        private static void ApplyEnvironmentOverrides(ServerConfig config)
        {
            string? contentPath = Environment.GetEnvironmentVariable("VALLEY_CONTENT_PATH");
            if (!string.IsNullOrWhiteSpace(contentPath))
            {
                config.Paths.ContentPath = contentPath!;
            }
        }

        /// <summary>
        /// Applies command line arguments. They win over both the file and the
        /// environment so a one-off launch can override a deployed configuration.
        /// </summary>
        private static void ApplyCommandLineOverrides(ServerConfig config, string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length)
                {
                    continue;
                }

                if (!int.TryParse(args[i + 1], out int port))
                {
                    Console.WriteLine($"[Config] Ignoring --port: '{args[i + 1]}' is not a number; keeping {config.Network.Port}.");
                    return;
                }
                if (port < 1024 || port > 65535)
                {
                    Console.WriteLine($"[Config] Ignoring --port {port}: outside [1024, 65535]; keeping {config.Network.Port}.");
                    return;
                }

                config.Network.Port = port;
                return;
            }
        }

        /// <summary>
        /// Repairs values that a JSON document can express but the server cannot use:
        /// explicit nulls for a whole section, and null strings.
        /// </summary>
        private static void Normalize(ServerConfig config)
        {
            if (config.Network == null)
            {
                Console.WriteLine("[Config] \"Network\" was null; using defaults.");
                config.Network = new ServerConfig.NetworkSection();
            }
            if (config.World == null)
            {
                Console.WriteLine("[Config] \"World\" was null; using defaults.");
                config.World = new ServerConfig.WorldSection();
            }
            if (config.Simulation == null)
            {
                Console.WriteLine("[Config] \"Simulation\" was null; using defaults.");
                config.Simulation = new ServerConfig.SimulationSection();
            }
            if (config.Paths == null)
            {
                Console.WriteLine("[Config] \"Paths\" was null; using defaults.");
                config.Paths = new ServerConfig.PathsSection();
            }

            config.World.FarmName = config.World.FarmName?.Trim() ?? "";
            config.World.HostName = config.World.HostName?.Trim() ?? "";
            config.Paths.ContentPath = config.Paths.ContentPath?.Trim() ?? "";
            config.Paths.SaveDirectory = config.Paths.SaveDirectory?.Trim() ?? "";
        }

        private static void Validate(ServerConfig config)
        {
            ServerConfig.NetworkSection network = config.Network;
            network.Port = RequireRange("Network.Port", network.Port, 1024, 65535, 24642);
            network.MaxConnections = RequireRange("Network.MaxConnections", network.MaxConnections, 1, 1024, 16);
            network.ConnectionTimeoutSeconds = RequireRange("Network.ConnectionTimeoutSeconds", network.ConnectionTimeoutSeconds, 1f, 600f, 30f);
            network.PingIntervalSeconds = RequireRange("Network.PingIntervalSeconds", network.PingIntervalSeconds, 0.5f, 600f, 5f);
            network.MaximumTransmissionUnit = RequireRange("Network.MaximumTransmissionUnit", network.MaximumTransmissionUnit, 512, 65535, 1200);

            ServerConfig.WorldSection world = config.World;
            world.FarmType = RequireRange("World.FarmType", world.FarmType, 0, 6, 0);
            world.FarmName = RequireText("World.FarmName", world.FarmName, "HeadlessFarm");
            world.HostName = RequireText("World.HostName", world.HostName, "Host");
            world.StartingCabins = RequireRange("World.StartingCabins", world.StartingCabins, 0, 32, 4);
            world.MaxFarmhands = RequireRange("World.MaxFarmhands", world.MaxFarmhands, 1, 32, 4);
            world.StarterParsnipSeeds = RequireRange("World.StarterParsnipSeeds", world.StarterParsnipSeeds, 0, 999, 15);

            config.Simulation.MillisecondsPerTenMinutes = RequireRange(
                "Simulation.MillisecondsPerTenMinutes", config.Simulation.MillisecondsPerTenMinutes, 1, 3600000, 1000);

            config.Paths.SaveDirectory = RequireText("Paths.SaveDirectory", config.Paths.SaveDirectory, "saved_farmhands");

            // Cross-section advisories: these combinations are legal but almost certainly
            // not what the operator intended, so they are reported without being changed.
            if (network.MaxConnections < world.MaxFarmhands)
            {
                Console.WriteLine($"[Config] Network.MaxConnections ({network.MaxConnections}) is below World.MaxFarmhands ({world.MaxFarmhands}); some farmhands may fail to connect.");
            }
            if (world.StartingCabins > world.MaxFarmhands)
            {
                Console.WriteLine($"[Config] World.StartingCabins ({world.StartingCabins}) exceeds World.MaxFarmhands ({world.MaxFarmhands}); extra cabins stay empty.");
            }
        }

        private static int RequireRange(string key, int value, int min, int max, int fallback)
        {
            if (value < min || value > max)
            {
                Console.WriteLine($"[Config] {key} = {value} is outside [{min}, {max}]; using {fallback}.");
                return fallback;
            }
            return value;
        }

        private static float RequireRange(string key, float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || value < min || value > max)
            {
                Console.WriteLine($"[Config] {key} = {value} is outside [{min}, {max}]; using {fallback}.");
                return fallback;
            }
            return value;
        }

        private static string RequireText(string key, string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine($"[Config] {key} is empty; using \"{fallback}\".");
                return fallback;
            }
            return value;
        }

        private static void LogEffectiveConfig(ServerConfig config)
        {
            ServerConfig.NetworkSection network = config.Network;
            ServerConfig.WorldSection world = config.World;
            string contentPath = string.IsNullOrWhiteSpace(config.Paths.ContentPath) ? "(auto)" : config.Paths.ContentPath;

            Console.WriteLine("[Config] Effective configuration:");
            Console.WriteLine($"[Config]   Network.Port={network.Port} Network.MaxConnections={network.MaxConnections} " +
                              $"Network.ConnectionTimeoutSeconds={network.ConnectionTimeoutSeconds} Network.PingIntervalSeconds={network.PingIntervalSeconds} " +
                              $"Network.MaximumTransmissionUnit={network.MaximumTransmissionUnit}");
            Console.WriteLine($"[Config]   World.FarmType={world.FarmType} World.FarmName={world.FarmName} World.HostName={world.HostName} " +
                              $"World.Seed={world.Seed} World.StartingCabins={world.StartingCabins} World.CabinsSeparate={world.CabinsSeparate} " +
                              $"World.MaxFarmhands={world.MaxFarmhands} World.StarterParsnipSeeds={world.StarterParsnipSeeds}");
            Console.WriteLine($"[Config]   Simulation.MillisecondsPerTenMinutes={config.Simulation.MillisecondsPerTenMinutes}");
            Console.WriteLine($"[Config]   Paths.ContentPath={contentPath} Paths.SaveDirectory={config.Paths.SaveDirectory}");
        }
    }
}
