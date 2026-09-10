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
                name: "stop",
                usage: "stop",
                description: "Save every farmhand, disconnect clients and exit.",
                handler: _ => RequestGracefulShutdown("the stop command"),
                aliases: new[] { "shutdown", "quit" }));
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
