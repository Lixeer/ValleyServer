#pragma warning disable SYSLIB0050

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using xTile.Dimensions;
using xTile.Display;
using xTile.Tiles;
using Lidgren.Network;
using Netcode;
using StardewValley;
using StardewValley.Network;
using StardewValley.Network.NetReady;
using StardewValley.Network.Dedicated;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Buffs;
using StardewValley.SaveSerialization;
using StardewValley.GameData.LocationContexts;
using StardewValley.GameData.Locations;
using StardewValley.Events;
namespace HeadlessServer
{
    partial class Program
    {
        private static readonly Dictionary<string, string> loggedLocationErrors = new Dictionary<string, string>();
        private static long lastDebrisDiagnosticsMs = -10000;
        private static readonly Dictionary<int, long> receivedMessageTypeCounts = new Dictionary<int, long>();
        private static readonly Dictionary<string, int> lastDebrisCountByLocation = new Dictionary<string, int>();
        private static bool updateLateErrorLogged = false;
        private static IEnumerator<int>? headlessNewDayProcess;
        private static Thread? headlessNewDayThread;
        private static volatile bool headlessNewDayActive;
        private static long lastNewDayMonitorMs;
        // Vanilla drives the overnight network pump from the overnight worker. Keep the
        // headless main loop from concurrently entering the same game/network state.

        private static void EnsureFarmhandHomesAndBeds(Farm farm)
        {
            foreach (var building in farm.buildings)
            {
                try { building.load(); } catch (Exception ex) { Console.WriteLine($"[FarmInit] Failed to load {building.buildingType.Value}: {ex.Message}"); }
                if (building.GetIndoors() is FarmHouse house && house.GetPlayerBed() == null)
                {
                    house.furniture.Add(new BedFurniture(BedFurniture.DEFAULT_BED_INDEX, new Vector2(9f, 8f)));
                    Console.WriteLine($"[FarmInit] Added fallback bed to {house.NameOrUniqueName}.");
                }
            }
            foreach (var farmer in Game1.otherFarmers.Values)
            {
                try
                {
                    bool assigned = Game1.netWorldState.Value.TryAssignFarmhandHome(farmer);
                    Console.WriteLine($"[FarmhandHome] {farmer.UniqueMultiplayerID} home={farmer.homeLocation.Value}, assigned={assigned}.");
                    if (Game1.getLocationFromName(farmer.homeLocation.Value) is FarmHouse home && home.GetPlayerBed() == null)
                        home.furniture.Add(new BedFurniture(BedFurniture.DEFAULT_BED_INDEX, new Vector2(9f, 8f)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FarmhandHome] Initialization failed for {farmer.UniqueMultiplayerID}: {ex.Message}");
                }
            }
        }

        // Spawns a temporary farmhand plus a wood drop within magnetic range, runs the same
        // location update the tick loop uses, and verifies the debris target assignment.
        // Server-side state is fully restored afterwards, so real clients see a pristine world.
        private static void RunDebrisSelfTest(Farm farm)
        {
            const long testFarmerId = 88888888L;
            Console.WriteLine("[SelfTest] Running debris target assignment self-test...");
            try
            {
                var testFarmer = new Farmer(new FarmerSprite(null), Vector2.Zero, 1, "SelfTest", Farmer.initialTools(), isMale: true)
                {
                    UniqueMultiplayerID = testFarmerId
                };
                testFarmer.currentLocation = farm;
                testFarmer.Position = new Vector2(64f * 64f, 15f * 64f); // farmhouse porch area
                var testRoot = new NetFarmerRoot(testFarmer);
                Game1.otherFarmers.Roots[testFarmerId] = testRoot;
                // NetFarmerRef resolves its target through Game1.getAllFarmers(), which in
                // 1.6.15 enumerates the world-state farmhandData directory, not otherFarmers.
                Game1.netWorldState.Value.farmhandData[testFarmerId] = testFarmer;

                Vector2 debrisOrigin = testFarmer.Position + new Vector2(64f, 0f); // within the 128px magnetic radius
                farm.debris.Add(new Debris("(O)388", 4, debrisOrigin, testFarmer.Position));

                var time = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
                for (int i = 0; i < 80; i++)
                {
                    time = new GameTime(time.TotalGameTime + TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16));
                    Game1.currentGameTime = time;
                    foreach (GameLocation location in Game1.locations)
                    {
                        if (location.farmers.Any())
                        {
                            location.UpdateWhenCurrentLocation(time);
                        }
                        location.updateEvenIfFarmerIsntHere(time);
                    }
                }

                Debris? tracked = farm.debris.FirstOrDefault();
                Farmer? assigned = tracked?.player.Value;
                if (assigned != null && assigned.UniqueMultiplayerID == testFarmerId)
                {
                    Console.WriteLine($"[SelfTest] Debris target assignment PASS: debris assigned to farmer {assigned.UniqueMultiplayerID} at {assigned.Position}.");
                }
                else
                {
                    Console.WriteLine($"[SelfTest] Debris target assignment FAIL: debrisCount={farm.debris.Count} assigned={(assigned == null ? "null" : assigned.UniqueMultiplayerID.ToString())}");
                    if (tracked != null)
                    {
                        Vector2 approx = Vector2.Zero;
                        foreach (Chunk chunk in tracked.Chunks)
                        {
                            approx += chunk.position.Value;
                        }
                        if (tracked.Chunks.Count > 0)
                        {
                            approx /= tracked.Chunks.Count;
                        }
                        Console.WriteLine($"[SelfTest]   debris: type={tracked.debrisType.Value} itemId={tracked.itemId.Value} item={(tracked.item == null ? "null" : tracked.item.QualifiedItemId)} chunks={tracked.Chunks.Count} attract={tracked.chunksMoveTowardPlayer} bounceTimer={tracked.timeSinceDoneBouncing} droppedBy={tracked.DroppedByPlayerID.Value} approxPos={approx}");
                        foreach (Farmer farmer in farm.farmers)
                        {
                            Point pixel = farmer.StandingPixel;
                            int radius = farmer.GetAppliedMagneticRadius();
                            bool inRange = Math.Abs(approx.X + 32f - pixel.X) <= radius && Math.Abs(approx.Y + 32f - pixel.Y) <= radius;
                            Console.WriteLine($"[SelfTest]   farmer {farmer.UniqueMultiplayerID}: pos={farmer.Position} standing={pixel} magnetRadius={radius} inRange={inRange} acceptsItem={farmer.couldInventoryAcceptThisItem(tracked.itemId.Value, 1, tracked.itemQuality)}");
                        }
                    }
                }

                // Restore pristine state before real clients connect.
                farm.debris.Clear();
                Game1.otherFarmers.Roots.Remove(testFarmerId);
                testFarmer.currentLocation = null;
                Console.WriteLine("[SelfTest] Cleaned up test farmer and debris.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SelfTest] Debris self-test crashed: {ex}");
                farm.debris.Clear();
                Game1.otherFarmers.Roots.Remove(testFarmerId);
            }
        }

        // Mirrors vanilla Game1.UpdateLocations/_UpdateLocation for the host: every location
        // updates each tick, and inhabited locations additionally run UpdateWhenCurrentLocation.
        // The debris target assignment happens there (master game), while pickup runs on the
        // owning client 鈥?see the call-site comment for the full vanilla flow.
        private static void UpdateHeadlessLocations(GameTime simulationTime)
        {
            foreach (GameLocation location in Game1.locations)
            {
                if (location == null)
                {
                    continue;
                }
                try
                {
                    bool shouldUpdate = location.farmers.Any();
                    if (shouldUpdate)
                    {
                        location.UpdateWhenCurrentLocation(simulationTime);
                    }
                    location.updateEvenIfFarmerIsntHere(simulationTime);
                }
                catch (Exception ex)
                {
                    string key = location.NameOrUniqueName;
                    if (!loggedLocationErrors.TryGetValue(key, out string? previous) || previous != ex.Message)
                    {
                        loggedLocationErrors[key] = ex.Message;
                        Console.WriteLine($"[LocationUpdate] {key}: {ex}");
                    }
                }
            }
            LogDebrisDiagnostics();
        }

        private static void LogDebrisDiagnostics()
        {
            long now = Environment.TickCount64;
            if (now - lastDebrisDiagnosticsMs < 5000)
            {
                return;
            }
            lastDebrisDiagnosticsMs = now;

            if (clientConnections.Count > 0)
            {
                var parts = receivedMessageTypeCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
                Console.WriteLine($"[MsgStats] received types: {string.Join(" ", parts)}");
            }

            int worldDebris = 0;
            foreach (GameLocation location in Game1.locations)
            {
                if (location == null)
                {
                    continue;
                }
                string key = location.NameOrUniqueName;
                int count = location.debris.Count;
                worldDebris += count;
                if (count == 0)
                {
                    lastDebrisCountByLocation[key] = 0;
                    continue;
                }

                int assigned = 0;
                var owners = new List<long>();
                foreach (Debris debris in location.debris)
                {
                    Farmer? target = debris.player.Value;
                    if (target != null)
                    {
                        assigned++;
                        owners.Add(target.UniqueMultiplayerID);
                    }
                }

                int last = lastDebrisCountByLocation.TryGetValue(key, out int lastValue) ? lastValue : 0;
                if (count > last)
                {
                    // Newly spawned debris appeared on the server since the last sample.
                    Debris? fresh = location.debris[last];
                    string where = fresh != null && fresh.Chunks.Count > 0 ? fresh.Chunks[0].position.Value.ToString() : "?";
                    Console.WriteLine($"[DebrisSpawn] {key}: +{count - last} now {count}; sample itemId={(fresh == null ? "?" : fresh.itemId.Value)} type={(fresh == null ? "?" : fresh.debrisType.Value.ToString())} chunk0={where} assigned={assigned}");
                }
                else if (count < last)
                {
                    Console.WriteLine($"[DebrisSpawn] {key}: -{last - count} (collected/expired) now {count}");
                }
                lastDebrisCountByLocation[key] = count;
                Console.WriteLine($"[DebrisDiag] {key}: debris={count} assigned={assigned} targets=[{string.Join(",", owners)}] farmers={location.farmers.Count()}");
            }

            if (clientConnections.Count > 0)
            {
                Console.WriteLine($"[DebrisDiag] world debris total={worldDebris}");
            }
        }

        private static void AdvanceHeadlessClock(long elapsedMilliseconds)
        {
            headlessClockAccumulatorMs += elapsedMilliseconds;
            while (headlessClockAccumulatorMs >= HeadlessMillisecondsPerTenMinutes)
            {
                headlessClockAccumulatorMs -= HeadlessMillisecondsPerTenMinutes;
                int minutes = Game1.timeOfDay % 100 + 10;
                int hours = Game1.timeOfDay / 100;
                if (minutes >= 60)
                {
                    hours++;
                    minutes -= 60;
                }
                Game1.timeOfDay = Math.Min(hours * 100 + minutes, 2600);
            }
        }

        private static bool hostSleepTriggered = false;
        private static int lastLogReady = -1, lastLogRequired = -1;

        private static void EnsureHeadlessDedicatedHostFlag()
        {
            try
            {
                FarmerTeam? team = Game1.player?.team;
                FieldInfo? flagField = typeof(FarmerTeam).GetField("hasDedicatedHost", BindingFlags.Instance | BindingFlags.NonPublic);
                if (team != null && flagField?.GetValue(team) is NetBool flag && !flag.Value)
                {
                    flag.Value = true;
                    Console.WriteLine("[HeadlessNewDay] Restored dedicated-host flag.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HeadlessNewDay] Failed to restore dedicated-host flag: {ex.Message}");
            }
        }

        private static void PrepareHeadlessHostForSleep()
        {
            if (Game1.dedicatedServer == null || Game1.netReady == null)
                return;
            if (!Game1.HasDedicatedHost)
                return;
            if (Game1.newDay)
                return;

            SyncDisconnectingFarmers();

            int ready = Game1.netReady.GetNumberReady("sleep");
            int required = Game1.netReady.GetNumberRequired("sleep");
            int activeClients = clientConnections.Count;
            if (ready <= 0)
            {
                hostSleepTriggered = false;
            }
            if (ready != lastLogReady || required != lastLogRequired)
            {
                lastLogReady = ready; lastLogRequired = required;
                Console.WriteLine($"[HeadlessNewDay][dbg] sleep ready={ready} required={required} activeClients={activeClients} hostTriggered={hostSleepTriggered} hostFarmInBed={Game1.player.isInBed.Value} hostLoc={Game1.currentLocation?.Name}");
            }

            // Headless server never runs the render-loop warp that a real dedicated host
            // relies on to move itself home and join the night sleep check. Reproduce the
            // vanilla dedicated-server steps once all real farmhands are in bed: place the
            // invisible host in its own farmhouse bed and answer the sleep prompt so the
            // host becomes sleep-ready.
            bool allActiveClientsReady = activeClients > 0 && ready >= activeClients;
            if (!Game1.newDay && allActiveClientsReady && !hostSleepTriggered)
            {
                Farmer host = Game1.player;
                host.isInBed.Value = true;
                host.timeWentToBed.Value = (int)Game1.timeOfDay;
                host.Halt();
                Console.WriteLine($"[HeadlessNewDay] All {activeClients} active client(s) ready (ready={ready}/{required}). Setting host sleep ready...");
                try
                {
                    Game1.netReady.SetLocalReady("sleep", true);
                    Game1.netReady.SetLocalReady("ready_for_save", true);
                    Game1.netReady.SetLocalReady("wakeup", true);
                    hostSleepTriggered = true;
                    Console.WriteLine($"[HeadlessNewDay] Host marked ready (ready={Game1.netReady.GetNumberReady("sleep")}/{Game1.netReady.GetNumberRequired("sleep")}, isReady={Game1.netReady.IsReady("sleep")}).");
                }
                catch (Exception ex)
                {
                    hostSleepTriggered = false;
                    Console.WriteLine($"[HeadlessNewDay] Host sleep setup failed: {ex}");
                }
            }

            // Drive the vanilla NewDay once readycheck finishes or when all active clients + host are in bed
            if (hostSleepTriggered && !Game1.newDay)
            {
                hostSleepTriggered = false;
                Console.WriteLine($"[HeadlessNewDay] Sleep check satisfied; starting NewDay (time {Game1.timeOfDay}, day {Game1.dayOfMonth})...");
                try
                {
                    Game1.NewDay(0f);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HeadlessNewDay] Game1.NewDay failed: {ex}");
                }
            }
        }

        private static void SyncDisconnectingFarmers()
        {
            try
            {
                var mp = typeof(Game1).GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) as Multiplayer;
                if (mp == null) return;
                var field = typeof(Multiplayer).GetField("disconnectingFarmers", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(mp) is List<long> list)
                {
                    list.Clear();
                    foreach (var id in Game1.otherFarmers.Roots.Keys)
                    {
                        if (!clientConnections.ContainsKey(id))
                        {
                            list.Add(id);
                        }
                    }
                }
            }
            catch { }
        }

        private static void PrepareHeadlessEndOfNightUi()
        {
            // Game1._newDayAfterFade eventually calls showEndOfNightStuff(). A real
            // dedicated host has a UI/content environment, but this host intentionally
            // does not load fonts. Pre-seed the menu stack with an uninitialized
            // SaveGameMenu so showEndOfNightStuff() pops a harmless placeholder instead
            // of invoking SaveGameMenu() -> SparklingText -> dialogueFont.MeasureString.
            try
            {
                Game1.endOfNightMenus ??= new Stack<StardewValley.Menus.IClickableMenu>();
                if (Game1.endOfNightMenus.Count == 0)
                {
                    var placeholder = (StardewValley.Menus.SaveGameMenu)FormatterServices.GetUninitializedObject(typeof(StardewValley.Menus.SaveGameMenu));
                    Game1.endOfNightMenus.Push(placeholder);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HeadlessNewDay] Failed to prepare headless end-of-night UI: {ex}");
            }
        }

        private static void PumpHeadlessNewDayProcess()
        {
            // In the retail game the overnight coroutine (_newDayAfterFade) runs on a
            // background Task (_newDayTask) while the main thread keeps pumping network
            // messages; the coroutine's barrier() calls block on ITS OWN thread waiting for
            // farmhand replies that only the main loop can receive. Mirror that model: if we
            // stepped the coroutine synchronously on the main thread, barrier() would block
            // the main loop and the server would deadlock with clients stuck on a black
            // screen waiting for the day roll to finish.
            if (!Game1.newDay || headlessNewDayThread != null)
            {
                return;
            }
            EnsureHeadlessDedicatedHostFlag();
            PrepareHeadlessEndOfNightUi();
            IEnumerator<int>? enumerator = null;
            try
            {
                MethodInfo? overnight = typeof(Game1).GetMethod(
                    "_newDayAfterFade", BindingFlags.Static | BindingFlags.NonPublic);
                if (overnight == null)
                    throw new MissingMethodException(typeof(Game1).FullName, "_newDayAfterFade");
                enumerator = (IEnumerator<int>?)overnight.Invoke(null, null);
                headlessNewDayProcess = enumerator;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HeadlessNewDay] Failed to start overnight coroutine: {ex}");
                return;
            }
            IEnumerator<int>? iterator = enumerator;
            Game1.newDay = true;
            headlessNewDayActive = true;
            Console.WriteLine(DescribeNewDayState("worker-starting"));
            headlessNewDayThread = new Thread(() =>
            {
                try
                {
                    // Keep the dedicated-host identity intact throughout the overnight
                    // coroutine. showEndOfNightStuff() must skip client UI on the server.
                    EnsureHeadlessDedicatedHostFlag();
                    Console.WriteLine(DescribeNewDayState("worker-enter"));
                    // The iterator blocks internally at each barrier() while it waits for
                    // the farmhand replies the main loop feeds into newDaySync; MoveNext
                    // therefore naturally paces the overnight sequence.
                    while (iterator != null && iterator.MoveNext())
                    {
                    }

                    // On dedicated/headless server, SaveGameMenu UI is bypassed, so ensure
                    // save synchronization flags and finish signals are sent to client farmhands.
                    Console.WriteLine(DescribeNewDayState("coroutine-returned"));
                    try
                    {
                        if (Game1.newDaySync != null && Game1.newDaySync.hasInstance())
                        {
                            if (!Game1.newDaySync.hasSaved())
                            {
                                Console.WriteLine(DescribeNewDayState("before-flagSaved"));
                                // The headless server skips SaveGameMenu.update(), so it never
                                // marks itself ready for the save/wakeup ReadyChecks the client
                                // runs after the save menu. Mirror the master menu branch here:
                                // mark ready_for_save, then broadcast the saved variable.
                                try { Game1.newDaySync.readyForSave(); } catch (Exception rEx) { Console.WriteLine($"[HeadlessNewDay] readyForSave failed: {rEx.Message}"); }
                                Game1.newDaySync.flagSaved();
                                Console.WriteLine("[HeadlessNewDay] Flagged newDaySync.flagSaved() for clients.");
                                Console.WriteLine(DescribeNewDayState("after-flagSaved"));
                            }
                            if (!Game1.newDaySync.hasFinished())
                            {
                                Console.WriteLine(DescribeNewDayState("before-finish"));
                                // Mark the host ready for the "wakeup" ReadyCheck, then send the
                                // finished variable. Without this the client's
                                // PollForEndOfNewDaySync() never sees wakeup ready and stays
                                // stuck on the black screen showing "waiting for players".
                                try { Game1.newDaySync.readyForFinish(); } catch (Exception rEx) { Console.WriteLine($"[HeadlessNewDay] readyForFinish failed: {rEx.Message}"); }
                                Game1.newDaySync.finish();
                                Console.WriteLine("[HeadlessNewDay] Flagged newDaySync.finish() for clients.");
                                Console.WriteLine(DescribeNewDayState("after-finish"));
                            }
                            Console.WriteLine(DescribeNewDayState("before-destroy"));
                            Game1.newDaySync.destroy();
                            Console.WriteLine(DescribeNewDayState("after-destroy"));
                        }
                    }
                    catch (Exception syncEx)
                    {
                        Console.WriteLine($"[HeadlessNewDay] Exception signaling newDaySync finish: {syncEx.Message}");
                    }

                    Console.WriteLine($"[HeadlessNewDay] Overnight coroutine finished at day {Game1.dayOfMonth}, time {Game1.timeOfDay}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HeadlessNewDay] Overnight coroutine failed: {ex}");
                }
                finally
                {
                    Game1.newDay = false;
                    headlessNewDayProcess = null;
                    headlessNewDayActive = false;
                    headlessNewDayThread = null;
                    try { Game1.timeOfDay = 600; Game1.netWorldState?.Value?.UpdateFromGame1(); } catch { }
                    Console.WriteLine($"[HeadlessNewDay] newDay cleared (time reset to {Game1.timeOfDay}).");
                    Console.WriteLine(DescribeNewDayState("worker-finally"));
                }
            })
            {
                IsBackground = true,
                Name = "HeadlessNewDay"
            };
            headlessNewDayThread.Start();
            Console.WriteLine("[HeadlessNewDay] Started vanilla overnight coroutine on background thread.");
        }

        private static int GetPort(string[] args)
        {
            const int defaultPort = 24642;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length)
                    continue;

                if (int.TryParse(args[i + 1], out int port) && port is >= 1024 and <= 65535)
                    return port;

                Console.WriteLine($"Warning: invalid --port value '{args[i + 1]}'; using {defaultPort}.");
                break;
            }

            return defaultPort;
        }

        private static string savedFarmhandsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_farmhands");
        private static HashSet<long> savedFarmerIds = new HashSet<long>();
        // The authoritative catalog of every farmhand that has ever been created/saved,
        // used to populate the character-selection list. It is intentionally kept separate
        // from Game1.otherFarmers, which only ever holds currently-online players and is
        // pruned by Multiplayer.removeDisconnectedFarmers during the day roll.
        private static readonly Dictionary<long, Farmer> savedFarmhandCatalog = new();

        static List<Farmer> LoadSavedFarmhands()
        {
            var list = new List<Farmer>();
            if (!Directory.Exists(savedFarmhandsPath))
            {
                Directory.CreateDirectory(savedFarmhandsPath);
            }

            foreach (var file in Directory.GetFiles(savedFarmhandsPath, "*.xml"))
            {
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                        var farmer = serializer.Deserialize(stream) as Farmer;
                        if (farmer != null && farmer.UniqueMultiplayerID != 99999999L && farmer.UniqueMultiplayerID != 0)
                        {
                            // Keep every saved farmhand in the selection catalog. Only live
                            // connections are registered into Game1.otherFarmers later (Message 2).
                            savedFarmhandCatalog[farmer.UniqueMultiplayerID] = farmer;
                            list.Add(farmer);
                            savedFarmerIds.Add(farmer.UniqueMultiplayerID);

                            // The world-state directory is a persistent directory of all farmhands
                            // in the world (like vanilla SaveGame does on load); it is not purged the
                            // way otherFarmers is, so it is safe to keep offline farmhands here for
                            // home/cabin resolution.
                            if (!Game1.netWorldState.Value.farmhandData.ContainsKey(farmer.UniqueMultiplayerID))
                            {
                                Game1.netWorldState.Value.farmhandData[farmer.UniqueMultiplayerID] = farmer;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading saved farmhand from {file}: {ex}");
                }
            }
            return list;
        }

        static void SaveFarmhand(Farmer farmer)
        {
            if (!Directory.Exists(savedFarmhandsPath))
            {
                Directory.CreateDirectory(savedFarmhandsPath);
            }

            string filePath = Path.Combine(savedFarmhandsPath, $"{farmer.UniqueMultiplayerID}.xml");
            string tempPath = filePath + ".tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                    serializer.Serialize(stream, farmer);
                    stream.Flush(true);
                }
                File.Move(tempPath, filePath, true);
                Console.WriteLine($"Successfully saved farmhand {farmer.Name} ({farmer.UniqueMultiplayerID}) to {filePath}");
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                Console.WriteLine($"Error saving farmhand {farmer.Name}: {ex.Message}");
            }
        }

        static void SaveAllActiveFarmhands()
        {
            foreach (var rootKvp in Game1.otherFarmers.Roots)
            {
                var farmer = rootKvp.Value.Value;
                if (farmer != null && farmer.UniqueMultiplayerID != 99999999L && farmer.UniqueMultiplayerID != 0 && farmer.isCustomized.Value)
                {
                    SaveFarmhand(farmer);
                }
            }
        }

        static void BroadcastMessage(OutgoingMessage outMsg, NetServer server, NetConnection? excludeConnection = null)
        {
            if (server.Connections.Count == 0) return;
            
            var msg = server.CreateMessage();
            MockLidgrenMessageUtils.WriteMessage(outMsg, msg);
            
            List<NetConnection> targets = new List<NetConnection>();
            foreach (var conn in server.Connections)
            {
                if (conn != excludeConnection && conn.Status == NetConnectionStatus.Connected)
                {
                    targets.Add(conn);
                }
            }

            if (targets.Count > 0)
            {
                server.SendMessage(msg, targets, NetDeliveryMethod.ReliableOrdered, 0);
            }
        }

        static byte[] WriteObjectFullBytes<T>(NetRoot<T> root, long peer) where T : class, INetObject<INetSerializable>
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    root.CreateConnectionPacket(writer, peer);
                    return stream.ToArray();
                }
            }
        }
    }
}
