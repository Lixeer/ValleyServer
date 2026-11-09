#pragma warning disable SYSLIB0050

using System;

namespace HeadlessServer
{
    /// <summary>
    /// Console command surface of the dedicated server: the command table, the built-in
    /// commands, and the main-loop pump that executes queued command lines.
    /// </summary>
    partial class Program
    {
        private static readonly ServerCommandRegistry commandRegistry = new ServerCommandRegistry();

        /// <summary>
        /// Registers the built-in console commands. Called once during startup, before
        /// the message loop begins.
        /// </summary>
        private static void RegisterBuiltInCommands()
        {
            commandRegistry.Register(new ServerCommand(
                name: "help",
                usage: "help",
                description: "List the available console commands.",
                handler: _ => commandRegistry.PrintHelp()));

            commandRegistry.Register(new ServerCommand(
                name: "saves",
                usage: "saves",
                description: "List the worlds available to load, and which one this run uses.",
                handler: _ => PrintSaves()));

            commandRegistry.Register(new ServerCommand(
                name: "save",
                usage: "save",
                description: "Write a native save now and report the resulting file path.",
                handler: _ =>
                {
                    Console.WriteLine("[Commands] Saving...");
                    Console.WriteLine(SaveNativeGame()
                        ? $"[Commands] Save finished: {nativeSaveFile}"
                        : "[Commands][ERROR] Save failed; see the [Save] lines above.");
                }));

            commandRegistry.Register(new ServerCommand(
                name: "world",
                usage: "world",
                description: "Print the object/terrain/building/furniture counts of the farm and of every farmhouse.",
                handler: _ => LogWorldSnapshot("command", force: true)));

            commandRegistry.Register(new ServerCommand(
                name: "resync",
                usage: "resync",
                description: "Re-send the full farm and farmhouse state to every connected farmhand.",
                handler: _ => ResyncLocationsToClients("command")));

            commandRegistry.Register(new ServerCommand(
                name: "stop",
                usage: "stop",
                description: "Save every farmhand, disconnect clients and exit.",
                handler: _ => RequestGracefulShutdown("the stop command"),
                aliases: new[] { "shutdown", "quit" }));
        }

        /// <summary>
        /// Lists the worlds on disk. The running world cannot be switched in place, because the
        /// game's static world state is injected once per process, so this reports the choice and
        /// how to act on it instead of pretending a reload happened.
        /// </summary>
        private static void PrintSaves()
        {
            Console.WriteLine("Worlds available to load:");
            var saves = SaveSelection.ListSaves();
            if (saves.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                for (int i = 0; i < saves.Count; i++)
                {
                    string marker = string.Equals(saves[i].FolderName, SelectedWorldFolder, StringComparison.OrdinalIgnoreCase) ? "  <- in use" : "";
                    Console.WriteLine($"  [{i + 1}] {saves[i].Describe()}{marker}");
                }
            }

            Console.WriteLine($"  Current run: {(string.IsNullOrWhiteSpace(SelectedWorldFolder) ? "(newest save)" : SelectedWorldFolder)}");
            Console.WriteLine("  A world is chosen at startup. Change Paths.WorldSave in config.json,");
            Console.WriteLine("  or start with '--save <folder>' / '--save new', then restart the server.");
        }

        /// <summary>
        /// Executes the commands typed on the console since the last pass. Invoked from
        /// the main loop so handlers observe a consistent game and network state.
        /// </summary>
        private static void PumpConsoleCommands()
        {
            ConsoleCommandReader.Drain(line =>
            {
                string[] tokens = ServerCommandRegistry.Tokenize(line);
                if (tokens.Length == 0)
                {
                    return;
                }

                ServerCommand? command = commandRegistry.Find(tokens[0]);
                if (command == null)
                {
                    Console.WriteLine($"[Commands] Unknown command '{tokens[0]}'. Type 'help' for the list.");
                    return;
                }

                string[] arguments = tokens.Length > 1 ? tokens[1..] : Array.Empty<string>();
                try
                {
                    command.Handler(arguments);
                }
                catch (Exception ex)
                {
                    // A misbehaving command must not take the server down with it.
                    Console.WriteLine($"[Commands] '{command.Name}' failed: {ex}");
                }
            });
        }
    }
}
