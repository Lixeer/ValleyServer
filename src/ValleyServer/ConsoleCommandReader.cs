#pragma warning disable SYSLIB0050

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace HeadlessServer
{
    /// <summary>
    /// Reads operator commands from standard input on a background thread and queues
    /// them for the main loop to execute. Queueing, rather than executing inline, keeps
    /// every command handler on the single thread that owns game and network state.
    ///
    /// The reader disables itself when standard input ends or is unusable, so a server
    /// started without a console (service manager, detached container, piped build
    /// step) still runs normally.
    /// </summary>
    public static class ConsoleCommandReader
    {
        private static readonly ConcurrentQueue<string> pendingLines = new ConcurrentQueue<string>();
        private static Thread? readerThread;
        private static volatile bool isActive;

        /// <summary>True while the reader thread is able to accept input.</summary>
        public static bool IsActive => isActive;

        /// <summary>Starts the reader thread once. Repeated calls are ignored.</summary>
        public static void Start()
        {
            if (readerThread != null)
            {
                return;
            }

            readerThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "ConsoleCommands"
            };
            readerThread.Start();
        }

        private static void ReadLoop()
        {
            try
            {
                isActive = true;
                while (true)
                {
                    string? line = Console.ReadLine();
                    if (line == null)
                    {
                        // End of input: no interactive console is attached.
                        isActive = false;
                        Console.WriteLine("[Commands] Standard input closed; console commands are unavailable.");
                        return;
                    }

                    if (line.Length > 0)
                    {
                        pendingLines.Enqueue(line);
                    }
                }
            }
            catch (Exception ex)
            {
                isActive = false;
                Console.WriteLine($"[Commands] Console input unavailable: {ex.Message}");
            }
        }

        /// <summary>
        /// Hands every queued line to <paramref name="execute"/> and clears the queue.
        /// Call from the main loop only.
        /// </summary>
        public static void Drain(Action<string> execute)
        {
            while (pendingLines.TryDequeue(out string? line))
            {
                execute(line);
            }
        }
    }
}
