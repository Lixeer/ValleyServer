#pragma warning disable SYSLIB0050

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HeadlessServer
{
    /// <summary>
    /// One operator command available on the server console.
    ///
    /// Handlers are invoked from the main loop thread, never from the reader thread,
    /// so a handler may safely touch game and network state.
    /// </summary>
    public sealed class ServerCommand
    {
        public ServerCommand(string name, string usage, string description, Action<string[]> handler, params string[] aliases)
        {
            Name = name;
            Usage = usage;
            Description = description;
            Handler = handler;
            Aliases = aliases ?? Array.Empty<string>();
        }

        /// <summary>Primary name typed on the console.</summary>
        public string Name { get; }

        /// <summary>Alternative names accepted for this command.</summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>Usage line shown by <c>help</c>.</summary>
        public string Usage { get; }

        /// <summary>One-line description shown by <c>help</c>.</summary>
        public string Description { get; }

        /// <summary>Executed on the main loop thread with the parsed arguments.</summary>
        public Action<string[]> Handler { get; }
    }

    /// <summary>
    /// Ordered set of console commands. Names and aliases are matched
    /// case-insensitively.
    /// </summary>
    public sealed class ServerCommandRegistry
    {
        private readonly List<ServerCommand> commands = new List<ServerCommand>();

        /// <summary>Registered commands, in registration order.</summary>
        public IReadOnlyList<ServerCommand> Commands => commands;

        /// <summary>
        /// Adds a command. The first definition of a name wins, so a duplicate is
        /// reported and ignored instead of silently shadowing the original.
        /// </summary>
        public void Register(ServerCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            if (Find(command.Name) != null)
            {
                Console.WriteLine($"[Commands] '{command.Name}' is already registered; keeping the existing definition.");
                return;
            }

            commands.Add(command);
        }

        /// <summary>Finds a command by name or alias, ignoring case.</summary>
        public ServerCommand? Find(string name)
        {
            foreach (ServerCommand command in commands)
            {
                if (string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
                foreach (string alias in command.Aliases)
                {
                    if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return command;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Splits a console line into arguments on whitespace. Double quotes group
        /// words, so <c>say "hello world"</c> produces two arguments. An empty quoted
        /// string is preserved as an empty argument.
        ///
        /// A byte order mark is ignored: terminals and pipes that emit UTF-8 with a BOM
        /// would otherwise prefix it to the first command name and make that command
        /// permanently unrecognisable.
        /// </summary>
        public static string[] Tokenize(string line)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
            {
                return Array.Empty<string>();
            }

            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasContent = false;

            foreach (char c in line)
            {
                if (c == '\uFEFF')
                {
                    continue;
                }
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    // Track that a (possibly empty) quoted argument was supplied.
                    hasContent = true;
                    continue;
                }
                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (hasContent)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        hasContent = false;
                    }
                    continue;
                }

                current.Append(c);
                hasContent = true;
            }

            if (hasContent)
            {
                tokens.Add(current.ToString());
            }
            return tokens.ToArray();
        }

        /// <summary>Prints every registered command with its usage and description.</summary>
        public void PrintHelp()
        {
            if (commands.Count == 0)
            {
                Console.WriteLine("[Commands] No commands are registered.");
                return;
            }

            int width = commands.Max(command => command.Usage.Length);
            Console.WriteLine($"[Commands] Available commands ({commands.Count}):");
            foreach (ServerCommand command in commands)
            {
                Console.WriteLine($"[Commands]   {command.Usage.PadRight(width)}  {command.Description}");
            }
        }
    }
}
