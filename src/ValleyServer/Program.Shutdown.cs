#pragma warning disable SYSLIB0050

using System;
using System.Threading;
using Lidgren.Network;

namespace HeadlessServer
{
    /// <summary>
    /// Graceful shutdown path shared by the <c>stop</c> command, Ctrl+C and process
    /// teardown.
    ///
    /// A request is only recorded when it arrives, from whatever thread raised it. The
    /// actual work runs on the main loop thread after the message loop has ended, which
    /// is the only thread allowed to touch game and network state.
    /// </summary>
    partial class Program
    {
        private static readonly CancellationTokenSource shutdownRequest = new CancellationTokenSource();
        private static volatile bool gracefulShutdownRequested;
        private static string gracefulShutdownReason = "";

        /// <summary>
        /// Asks the message loop to stop. Safe to call from any thread, including the
        /// console reader thread and the Ctrl+C handler.
        /// </summary>
        internal static void RequestGracefulShutdown(string reason)
        {
            if (gracefulShutdownRequested)
            {
                Console.WriteLine($"[Shutdown] Already shutting down ({gracefulShutdownReason}); ignoring '{reason}'.");
                return;
            }

            gracefulShutdownRequested = true;
            gracefulShutdownReason = reason;
            Console.WriteLine($"[Shutdown] Requested by {reason}.");

            try
            {
                shutdownRequest.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Main already finished and disposed the source; nothing left to stop.
            }
        }

        /// <summary>
        /// Saves every connected farmhand and closes the Lidgren server. Runs on the
        /// main thread once the message loop has ended.
        /// </summary>
        private static void PerformGracefulShutdown(NetServer server)
        {
            Console.WriteLine($"[Shutdown] Shutting down gracefully ({gracefulShutdownReason}).");

            if (headlessNewDayActive)
            {
                // The overnight coroutine blocks on farmhand barriers that only the
                // message loop can answer, and that loop is already gone, so waiting for
                // it would only stall the exit. Report the interruption instead.
                Console.WriteLine("[Shutdown] A day roll is in progress; it will be interrupted, not completed.");
            }

            try
            {
                if (clientConnections.Count > 0)
                {
                    Console.WriteLine($"[Shutdown] Saving {clientConnections.Count} active farmhand(s)...");
                    SaveAllActiveFarmhands();
                }
                else
                {
                    Console.WriteLine("[Shutdown] No active farmhands to save.");
                }
            }
            catch (Exception ex)
            {
                // A failed save must not stop the server from releasing its socket.
                Console.WriteLine($"[Shutdown] Saving farmhands failed: {ex}");
            }

            try
            {
                // Tells every connected client why the connection is ending before the
                // socket closes, instead of dropping them into a timeout.
                server.Shutdown("Server is shutting down");
                Console.WriteLine("[Shutdown] Lidgren server stopped; clients were notified.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shutdown] Stopping the server failed: {ex}");
            }

            Console.WriteLine("[Shutdown] Done.");
        }
    }
}
