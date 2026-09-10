#pragma warning disable SYSLIB0050

using System;

namespace HeadlessServer
{
    /// <summary>
    /// Layered, file-backed configuration for the headless dedicated server.
    /// Values are read from <c>config.json</c> beside the executable by
    /// <see cref="ConfigLoader"/>; a default file is written on first run.
    ///
    /// Every property keeps the historical hard-coded value of the server as its
    /// default, so behaviour is unchanged when the file is absent, empty or partial.
    /// </summary>
    public sealed class ServerConfig
    {
        /// <summary>
        /// Effective configuration of the current process. Assigned once by
        /// <see cref="ConfigLoader.Load()"/> before game state is mocked.
        /// </summary>
        public static ServerConfig Current { get; internal set; } = new ServerConfig();

        /// <summary>Lidgren transport settings.</summary>
        public NetworkSection Network { get; set; } = new NetworkSection();

        /// <summary>State of the world created when the server starts.</summary>
        public WorldSection World { get; set; } = new WorldSection();

        /// <summary>Simulation pacing.</summary>
        public SimulationSection Simulation { get; set; } = new SimulationSection();

        /// <summary>Filesystem locations.</summary>
        public PathsSection Paths { get; set; } = new PathsSection();

        /// <summary>Lidgren transport settings.</summary>
        public sealed class NetworkSection
        {
            /// <summary>UDP port the server listens on. Overridden by <c>--port</c>.</summary>
            public int Port { get; set; } = 24642;

            /// <summary>Maximum number of simultaneous Lidgren connections.</summary>
            public int MaxConnections { get; set; } = 16;

            /// <summary>Seconds without traffic before a peer is dropped.</summary>
            public float ConnectionTimeoutSeconds { get; set; } = 30f;

            /// <summary>Seconds between keep-alive pings.</summary>
            public float PingIntervalSeconds { get; set; } = 5f;

            /// <summary>Maximum transmission unit, in bytes.</summary>
            public int MaximumTransmissionUnit { get; set; } = 1200;
        }

        /// <summary>State of the world created when the server starts.</summary>
        public sealed class WorldSection
        {
            /// <summary>Farm type index assigned to <c>Game1.whichFarm</c> (0 = standard).</summary>
            public int FarmType { get; set; } = 0;

            /// <summary>Farm name shown to clients.</summary>
            public string FarmName { get; set; } = "HeadlessFarm";

            /// <summary>Name of the built-in host farmer.</summary>
            public string HostName { get; set; } = "Host";

            /// <summary>World seed / unique game id. 0 derives a value from the current time.</summary>
            public ulong Seed { get; set; } = 0;

            /// <summary>Number of farmhand cabins created with the world.</summary>
            public int StartingCabins { get; set; } = 4;

            /// <summary>Whether farmhand cabins use the separate layout.</summary>
            public bool CabinsSeparate { get; set; } = false;

            /// <summary>Upper bound of simultaneously selectable farmhands.</summary>
            public int MaxFarmhands { get; set; } = 4;

            /// <summary>Parsnip seeds handed to a newly created farmhand.</summary>
            public int StarterParsnipSeeds { get; set; } = 15;
        }

        /// <summary>Simulation pacing.</summary>
        public sealed class SimulationSection
        {
            /// <summary>
            /// Real milliseconds of server time that advance the in-game clock by ten
            /// minutes. Lower values make the world day pass faster.
            /// </summary>
            public int MillisecondsPerTenMinutes { get; set; } = 1000;
        }

        /// <summary>Filesystem locations.</summary>
        public sealed class PathsSection
        {
            /// <summary>
            /// Directory holding the game Content assets. Empty auto-detects, and the
            /// <c>VALLEY_CONTENT_PATH</c> environment variable takes precedence.
            /// </summary>
            public string ContentPath { get; set; } = "";

            /// <summary>
            /// Directory holding saved farmhand XML files. A relative path resolves
            /// against the executable directory.
            /// </summary>
            public string SaveDirectory { get; set; } = "saved_farmhands";
        }
    }
}
