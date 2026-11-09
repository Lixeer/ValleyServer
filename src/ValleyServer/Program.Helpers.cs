#pragma warning disable SYSLIB0050

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
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
using StardewValley.Pathfinding;
namespace HeadlessServer
{
    partial class Program
    {
        private static readonly Dictionary<string, string> loggedLocationErrors = new Dictionary<string, string>();
        private static long lastDebrisDiagnosticsMs = -10000;
        private static readonly Dictionary<int, long> receivedMessageTypeCounts = new Dictionary<int, long>();
        private static readonly Dictionary<string, int> lastDebrisCountByLocation = new Dictionary<string, int>();
        private static bool updateLateErrorLogged = false;
        private static volatile IEnumerator<int>? headlessNewDayProcess;
        private static volatile Thread? headlessNewDayThread;
        private static volatile bool headlessNewDayActive;
        private static long lastNewDayMonitorMs;
        // Vanilla drives the overnight network pump from the overnight worker. Keep the
        // headless main loop from concurrently entering the same game/network state.

        // ---- world content guard -------------------------------------------------------
        // A farmhand reported the farm losing every object/terrain/building and a farmhouse
        // rendering half its furniture after the first night spent in the host's house. Both
        // are "the client's copy of a location is empty or was rebuilt" symptoms, and neither
        // the server-side code nor the netcode path showed an obvious writer. Log every change
        // of those counts so the exact moment of a wipe (and whether it happens on the server
        // at all) becomes visible in the console instead of being noticed by the player.
        private static string? lastWorldSnapshot;
        private static long lastWorldSnapshotMs;

        static void LogWorldSnapshot(string tag, bool force = false)
        {
            try
            {
                Farm? farm = Game1.getFarm();
                if (farm == null)
                {
                    return;
                }

                var parts = new List<string> { DescribeLocationContents(farm) };
                foreach (GameLocation location in Game1.locations)
                {
                    if (location is FarmHouse house)
                    {
                        parts.Add(DescribeLocationContents(house));
                    }
                }

                string snapshot = string.Join(" | ", parts);
                if (!force && snapshot == lastWorldSnapshot)
                {
                    return;
                }

                lastWorldSnapshot = snapshot;
                Console.WriteLine($"[WorldSnap:{tag}] {snapshot}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldSnap:{tag}][ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        }

        static string DescribeLocationContents(GameLocation location)
        {
            int crops = 0;
            foreach (StardewValley.TerrainFeatures.TerrainFeature feature in location.terrainFeatures.Values)
            {
                if (feature is StardewValley.TerrainFeatures.HoeDirt dirt && dirt.crop != null)
                {
                    crops++;
                }
            }

            int id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(location);
            bool canonical = ReferenceEquals(Game1.getLocationFromName(location.NameOrUniqueName), location);
            NetRoot<GameLocation>? unusedRoot = location.Root;
            return $"{location.NameOrUniqueName}({location.GetType().Name}#{id:x}) obj={location.objects.Count()} "
                + $"terr={location.terrainFeatures.Count()} crops={crops} bld={location.buildings.Count} "
                + $"furn={location.furniture.Count} chars={location.characters.Count} "
                + $"always={location.isAlwaysActive.Value} canon={canonical} "
                + $"root={(unusedRoot == null ? "none" : ReferenceEquals(unusedRoot.Value, location) ? "self" : "other")}";
        }

        /// <summary>
        /// Re-sends the complete canonical state of the farm and of every farmhouse interior to each
        /// connected farmhand.
        ///
        /// After the initial send, locations are synchronized with incremental deltas (message 6)
        /// that only carry the entries that changed. A client that misapplies a single delta keeps
        /// the resulting hole forever, which is what a farmhand reported after the first night spent
        /// in the host's house: the farm came back with no crops, no tilled soil and no placed
        /// buildings, and the main house showed its furniture scrambled. A full root (message 3)
        /// replaces the client's copy outright, so re-sending it after the day roll repairs whatever
        /// the deltas corrupted without the player having to reconnect.
        /// </summary>
        static void ResyncLocationsToClients(string reason)
        {
            try
            {
                if (Game1.server == null || Game1.otherFarmers.Count == 0)
                {
                    return;
                }

                var locations = new List<GameLocation>();
                Farm? farm = Game1.getFarm();
                if (farm != null)
                {
                    locations.Add(farm);
                }
                foreach (GameLocation location in Game1.locations)
                {
                    if (location is FarmHouse && !locations.Contains(location))
                    {
                        locations.Add(location);
                    }
                }
                if (locations.Count == 0)
                {
                    return;
                }

                var multiplayer = typeof(Game1).GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) as Multiplayer;
                if (multiplayer == null)
                {
                    return;
                }

                foreach (Farmer farmer in Game1.otherFarmers.Values)
                {
                    foreach (GameLocation location in locations)
                    {
                        try
                        {
                            NetRoot<GameLocation>? root = multiplayer.locationRoot(location);
                            if (root == null)
                            {
                                continue;
                            }
                            byte[] bytes = WriteObjectFullBytes(root, farmer.UniqueMultiplayerID);
                            var message = new OutgoingMessage(
                                3,
                                Game1.player.UniqueMultiplayerID,
                                new object[] { false, bytes });
                            Game1.server.sendMessage(farmer.UniqueMultiplayerID, message);
                            Console.WriteLine($"[Resync] {farmer.Name} ({farmer.UniqueMultiplayerID}): re-sent '{location.NameOrUniqueName}' ({bytes.Length} bytes) [{reason}].");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Resync] Failed to re-send '{location.NameOrUniqueName}' to {farmer.UniqueMultiplayerID}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Resync] Failed [{reason}]: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Hands a farmhand the configured starter seeds, unless it already carries them.
        /// A configured count of 0 disables the hand-out entirely.
        /// </summary>
        private static void GiveStarterParsnipSeeds(Farmer farmhand)
        {
            int count = ServerConfig.Current.World.StarterParsnipSeeds;
            if (count <= 0)
            {
                return;
            }
            if (farmhand.Items.Any(item => item != null && item.QualifiedItemId == "(O)472"))
            {
                return;
            }

            farmhand.Items.Add(ItemRegistry.Create("(O)472", count));
            Console.WriteLine($"Added {count} starter parsnip seeds to farmhand {farmhand.Name} ({farmhand.UniqueMultiplayerID}).");
        }

        /// <summary>
        /// Whether the farmhand's home is a cabin that is assigned to them (or a cabin they can take).
        /// The host's farmhouse is a valid FarmHouse but never a farmhand home, so checking the type
        /// alone let every farmhand keep "FarmHouse" and wake up in the host's bedroom.
        /// </summary>
        static bool HasOwnCabin(Farmer farmer)
        {
            if (farmer.IsMainPlayer)
                return true;
            if (string.IsNullOrEmpty(farmer.homeLocation.Value))
                return false;

            GameLocation? home = Game1.getLocationFromName(farmer.homeLocation.Value);
            if (home is not Cabin cabin)
                return false;

            // Compare by id, not by the cabin's cached Farmer reference: a cabin restored from a
            // native save can still reference a deserialized copy of this farmhand rather than the
            // instance the server works with, and object identity then rejects the real owner.
            Farmer? owner = cabin.owner;
            return owner != null && owner.UniqueMultiplayerID == farmer.UniqueMultiplayerID;
        }

        /// <summary>
        /// Writes the ownership both ways: the cabin references the farmhand instance the server
        /// uses (not a stale deserialized copy) and the farmhand points its homeLocation at the cabin.
        /// </summary>
        static void BindCabin(Cabin cabin, Farmer farmer)
        {
            try
            {
                cabin.farmhandReference.Value = farmer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FarmhandHome] Binding '{cabin.NameOrUniqueName}' to {farmer.Name} failed: {ex.Message}");
            }
            farmer.homeLocation.Value = cabin.NameOrUniqueName;
        }

        /// <summary>
        /// Assigns a cabin to a farmhand that has none, without the sleeping-location shortcut in
        /// <c>NetWorldState.TryAssignFarmhandHome</c>. That method prefers the cabin the farmer last
        /// slept in, so a visit to another player's cabin silently becomes their new home; the
        /// overnight <c>saveFarmhands</c> pass then teleports them there on wake-up.
        /// </summary>
        static bool AssignFarmhandCabin(Farmer farmer)
        {
            try
            {
                if (HasOwnCabin(farmer))
                    return true;

                // Keep the cabin the farmhand already claims when it is genuinely free, so a
                // restart does not shuffle everyone between cabins.
                string claimed = farmer.homeLocation.Value;
                if (!string.IsNullOrEmpty(claimed) && Game1.getLocationFromName(claimed) is Cabin own
                    && (own.owner == null || own.owner.UniqueMultiplayerID == farmer.UniqueMultiplayerID))
                {
                    BindCabin(own, farmer);
                    return true;
                }

                farmer.homeLocation.Value = "";

                string savedLastSleep = farmer.lastSleepLocation.Value;
                try
                {
                    // Hide the sleeping location for the duration of the assignment so the vanilla
                    // helper cannot adopt it as the new home.
                    farmer.lastSleepLocation.Value = "";
                    Game1.netWorldState.Value.TryAssignFarmhandHome(farmer);
                    if (HasOwnCabin(farmer))
                        return true;
                }
                finally
                {
                    farmer.lastSleepLocation.Value = savedLastSleep;
                }

                // The vanilla helper is unreliable here (it may hand back a cabin owned by someone
                // else); take the first cabin that nobody owns or that already belongs to this farmer.
                foreach (var building in Game1.getFarm().buildings)
                {
                    if (building.GetIndoors() is not Cabin cabin)
                        continue;
                    Farmer? owner = cabin.owner;
                    if (owner != null && owner.UniqueMultiplayerID != farmer.UniqueMultiplayerID && !owner.isUnclaimedFarmhand)
                        continue;

                    BindCabin(cabin, farmer);
                    return HasOwnCabin(farmer);
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FarmhandHome] Assigning a cabin to {farmer.Name} failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Drops location references that this world does not contain. Cabins are rebuilt with fresh
        /// unique names on every start, so a farmhand restored from disk can point at a cabin that no
        /// longer exists. The client then dies on the day roll inside
        /// <c>BedFurniture.ApplyWakeUpPosition</c> -&gt; <c>Game1.RequireLocation</c> with
        /// "Required location '&lt;old id&gt;' not found", which surfaces as the player being kicked
        /// out mid-sleep. Repair every such reference before the farmhand is sent to a client or does
        /// the overnight wake-up.
        /// </summary>
        static void SanitizeFarmhandLocations(Farmer farmer)
        {
            try
            {
                // A farmhand's home must be the cabin that actually belongs to them. "FarmHouse" (the
                // host's house on the main farm) is a valid FarmHouse, so the old check accepted it
                // and every farmhand woke up in the host's bedroom. Cabin ownership is what decides
                // where the overnight pass puts them.
                if (!HasOwnCabin(farmer))
                {
                    string stale = farmer.homeLocation.Value;

                    // Let the world state hand out a cabin, but never let it pick one based on where
                    // the farmer happens to be sleeping: TryAssignFarmhandHome prefers the cabin the
                    // farmer last slept in, which silently moves them into whichever cabin hosted
                    // them that night.
                    bool assigned = AssignFarmhandCabin(farmer);
                    string now = farmer.homeLocation.Value;
                    if (assigned && string.Equals(now, stale, StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"[FarmhandHome] {farmer.Name} ({farmer.UniqueMultiplayerID}) keeps cabin '{now}' (ownership recorded).");
                    else if (assigned)
                        Console.WriteLine($"[FarmhandHome] {farmer.Name} ({farmer.UniqueMultiplayerID}) home '{stale}' was not their own cabin; reassigned to '{now}'.");
                    else
                        Console.WriteLine($"[FarmhandHome] {farmer.Name} ({farmer.UniqueMultiplayerID}) has no usable cabin - they will wake up in the host's house.");
                }

                // lastSleepLocation only steers the wake-up spot; a dangling one is harmless but
                // makes the client log errors and fall back, so point it at the home cabin.
                if (!string.IsNullOrEmpty(farmer.lastSleepLocation.Value)
                    && Game1.getLocationFromName(farmer.lastSleepLocation.Value) == null)
                {
                    Console.WriteLine($"[FarmhandHome] {farmer.Name} ({farmer.UniqueMultiplayerID}) lastSleepLocation '{farmer.lastSleepLocation.Value}' is stale; cleared.");
                    farmer.lastSleepLocation.Value = "";
                }

                if (!string.IsNullOrEmpty(farmer.disconnectLocation.Value)
                    && Game1.getLocationFromName(farmer.disconnectLocation.Value) == null)
                {
                    farmer.disconnectLocation.Value = "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FarmhandHome] Sanitizing {farmer.Name} ({farmer.UniqueMultiplayerID}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Spawns the villagers from <c>Data/Characters</c> into their home locations.
        /// Vanilla does this in <c>Game1.AddNPCs()</c> at game start; the headless host skipped it,
        /// so the world had no villagers and every town interaction (talk, shop, friendship) had
        /// nobody to talk to. <see cref="HeadlessContentManager"/> already returns inert textures,
        /// so the portrait/sprite loads in the NPC constructor do not need a graphics device.
        /// </summary>
        static void AddNpcsToWorld()
        {
            int before = Game1.locations.Sum(loc => loc?.characters?.Count ?? 0);
            try
            {
                Game1.AddNPCs();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NPC][ERROR] Game1.AddNPCs failed: {ex.Message}");
            }

            int after = Game1.locations.Sum(loc => loc?.characters?.Count ?? 0);
            Console.WriteLine($"[NPC] Villagers in the world: {after} (was {before}).");
            if (after == before)
            {
                // Never silently ship a world without villagers: report which ones could not spawn.
                var names = Game1.characterData?.Where(kv => kv.Value.SpawnIfMissing).Select(kv => kv.Key).Take(10) ?? Enumerable.Empty<string>();
                Console.WriteLine($"[NPC][WARN] No villagers were added; SpawnIfMissing entries included: {string.Join(", ", names)}");
            }

            PopulateNpcWarpRoutes();
        }

        /// <summary>
        /// Builds the location-to-location route table NPCs use to walk between maps
        /// (<see cref="WarpPathfindingCache"/>). Vanilla calls this from <c>Game1.loadForNewGame</c>
        /// right after <c>AddNPCs()</c>, but the server loads a native save instead and never ran it,
        /// so the table stayed empty. Every cross-location schedule entry then failed to resolve a
        /// route: <c>NPC.pathfindToNextScheduleLocation</c> got null back from
        /// <c>WarpPathfindingCache.GetLocationRoute</c> and pathfound inside the villager's current
        /// map toward a tile that lives in another one, produced an empty path, and the controller
        /// was dropped on the floor. Villagers walked around inside their homes and never left.
        /// </summary>
        static void PopulateNpcWarpRoutes()
        {
            try
            {
                WarpPathfindingCache.PopulateCache();
                string[] probe = WarpPathfindingCache.GetLocationRoute("HaleyHouse", "Town", Gender.Female) ?? Array.Empty<string>();
                Console.WriteLine($"[NPC] Warp route table populated (HaleyHouse -> Town: {(probe.Length > 0 ? string.Join(" > ", probe) : "NOT FOUND")}).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NPC][ERROR] WarpPathfindingCache.PopulateCache failed: {ex}");
            }
        }

        /// <summary>
        /// Gives every villager today's schedule, the way an ordinary day rollover does.
        ///
        /// <para>
        /// Vanilla only loads schedules in <c>Game1</c>'s overnight block:
        /// <c>location.DayUpdate(dayOfMonth)</c> followed by <c>npc.dayUpdate(dayOfMonth)</c>. And
        /// <c>NPC.dayUpdate</c> bails out early unless <c>Game1.newDay</c> is set (or the game runs in
        /// save-load mode 6):
        /// </para>
        /// <code>if (onlyAppearance || (!Game1.newDay &amp;&amp; Game1.gameMode != 6)) return;</code>
        /// <para>
        /// A server that restores a native save never runs that block, so <c>NPC.Schedule</c> stayed
        /// empty, <c>checkSchedule</c> had nothing to do, and the whole town stood in its bedroom
        /// from morning to night. This replays just the villager part with the same guard satisfied.
        /// </para>
        /// </summary>
        static void InitializeVillagerDay()
        {
            bool previousNewDay = Game1.newDay;
            try
            {
                Game1.newDay = true;
                foreach (GameLocation location in Game1.locations)
                {
                    if (location == null) continue;
                    try { location.ResetCharacterDialogues(); }
                    catch (Exception ex) { Console.WriteLine($"[NPC][WARN] ResetCharacterDialogues failed for {location.NameOrUniqueName}: {ex.Message}"); }
                }
                foreach (NPC npc in Utility.getAllCharacters())
                {
                    if (npc == null) continue;
                    try
                    {
                        if (npc.IsVillager)
                        {
                            npc.islandScheduleName.Value = null;
                            npc.currentScheduleDelay = 0f;
                        }
                        npc.dayUpdate(Game1.dayOfMonth);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[NPC][WARN] dayUpdate failed for {npc.Name}: {ex.Message}");
                    }
                }
            }
            finally
            {
                Game1.newDay = previousNewDay;
            }

            int scheduled = 0;
            int runners = 0;
            var missing = new List<string>();
            foreach (NPC npc in Utility.getAllCharacters())
            {
                if (npc?.IsVillager != true) continue;
                if (npc.Schedule != null && npc.Schedule.Count > 0) scheduled++;
                else missing.Add(npc.Name);
                if (npc.followSchedule) runners++;
            }
            Console.WriteLine($"[NPC] Schedules loaded for {scheduled} villagers on {Game1.currentSeason} {Game1.dayOfMonth} (followSchedule={runners}).");
            if (missing.Count > 0)
                Console.WriteLine($"[NPC] Without a schedule today: {string.Join(", ", missing.Take(12))}{(missing.Count > 12 ? " ..." : "")}");
        }


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

            // Farmhands restored from disk are not in otherFarmers yet at startup, so repair the
            // catalog as well: otherwise the first client to pick one of them receives a stale
            // homeLocation and crashes on its first night.
            foreach (var cached in savedFarmhandCatalog.Values)
                SanitizeFarmhandLocations(cached);

            foreach (var farmer in Game1.otherFarmers.Values)
            {
                try
                {
                    SanitizeFarmhandLocations(farmer);
                    Console.WriteLine($"[FarmhandHome] {farmer.UniqueMultiplayerID} home={farmer.homeLocation.Value}.");
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
                // The self-test farmer is registered in the world farmhand directory so the location
                // update can resolve it; leaving it there made it a "saved" farmhand that showed up
                // in the selection list and claimed a cabin.
                Game1.netWorldState.Value.farmhandData.Remove(testFarmerId);
                testFarmer.currentLocation = null;
                Console.WriteLine("[SelfTest] Cleaned up test farmer and debris.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SelfTest] Debris self-test crashed: {ex}");
                farm.debris.Clear();
                Game1.otherFarmers.Roots.Remove(testFarmerId);
                Game1.netWorldState.Value.farmhandData.Remove(testFarmerId);
            }
        }

        // Mirrors vanilla Game1.UpdateLocations/_UpdateLocation for the host: every location
        // updates each tick, and inhabited locations additionally run UpdateWhenCurrentLocation.
        // The debris target assignment happens there (master game), while pickup runs on the
        // owning client - see the call-site comment for the full vanilla flow.
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
            // Configured pacing: real milliseconds that advance the in-game clock by ten
            // minutes. Read per call so a single source of truth drives the whole loop.
            long millisecondsPerTenMinutes = ServerConfig.Current.Simulation.MillisecondsPerTenMinutes;
            headlessClockAccumulatorMs += elapsedMilliseconds;
            while (headlessClockAccumulatorMs >= millisecondsPerTenMinutes)
            {
                headlessClockAccumulatorMs -= millisecondsPerTenMinutes;

                int minutes = Game1.timeOfDay % 100 + 10;
                int hours = Game1.timeOfDay / 100;
                if (minutes >= 60)
                {
                    hours++;
                    minutes -= 60;
                }
                Game1.timeOfDay = Math.Min(hours * 100 + minutes, 2600);

                // Vanilla advance the world in ten-minute steps (Game1.cs "Update10Minute"):
                // every location runs performTenMinuteUpdate(), which is what calls
                // NPC.checkSchedule(timeOfDay) for each villager. This is the ONLY schedule
                // driver in the shipped game (Game1.addHour() is wired to the F5 debug keys),
                // and it has to run on every ten-minute tick: NPC.checkSchedule ignores any
                // time below the last one it was handed (lastAttemptedSchedule), so calling it
                // only at whole hours permanently skips every mid-hour slot such as Haley's
                // "950 HaleyHouse 8 7 1" and she then never leaves the house all day.
                RunTenMinuteUpdate();
            }
        }

        /// <summary>
        /// Mirrors vanilla's ten-minute world tick (the <c>Update10Minute</c> block in Game1):
        /// every location runs <see cref="GameLocation.performTenMinuteUpdate"/>, which is what
        /// schedules villagers (<c>NPC.checkSchedule</c>) and ages machines, crops and furniture.
        /// The headless clock writes <c>timeOfDay</c> directly, so this has to be driven explicitly.
        /// </summary>
        private static void RunTenMinuteUpdate()
        {
            foreach (GameLocation location in Game1.locations)
            {
                if (location == null) continue;
                try
                {
                    GameLocation current = location;
                    if (Game1.currentLocation != null
                        && current.NameOrUniqueName == Game1.currentLocation.NameOrUniqueName)
                    {
                        current = Game1.currentLocation;
                    }
                    current.performTenMinuteUpdate(Game1.timeOfDay);
                    current.timeUpdate(10);
                }
                catch (Exception ex)
                {
                    string key = location.NameOrUniqueName;
                    if (!loggedTenMinuteErrors.TryGetValue(key, out string? previous) || previous != ex.Message)
                    {
                        loggedTenMinuteErrors[key] = ex.Message;
                        Console.WriteLine($"[TenMinute] {key}: {ex}");
                    }
                }
            }

            try { MineShaft.UpdateMines10Minutes(Game1.timeOfDay); }
            catch (Exception ex) { Console.WriteLine($"[TenMinute] MineShaft: {ex.Message}"); }
            try { VolcanoDungeon.UpdateLevels10Minutes(Game1.timeOfDay); }
            catch (Exception ex) { Console.WriteLine($"[TenMinute] VolcanoDungeon: {ex.Message}"); }
        }

        private static readonly Dictionary<string, string> loggedTenMinuteErrors = new();

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

        static int TileX(Farmer farmer) => (int)(farmer.Position.X / 64f);
        static int TileY(Farmer farmer) => (int)(farmer.Position.Y / 64f);

        /// <summary>
        /// A walkable tile for spawning in <paramref name="location"/>. (0,0) is solid on most maps,
        /// so a farmer left at the origin is stuck in the corner; this prefers the location's entry
        /// point and otherwise scans outward for a free tile.
        /// </summary>
        static Point GetSafeSpawnPoint(GameLocation location)
        {
            try
            {
                // FarmHouse exposes an entry location; for a Farm the sensible anchor is the door of
                // the farmhouse that was just built.
                Point entry = location is FarmHouse house
                    ? house.getEntryLocation()
                    : GetFarmhouseDoor(location) ?? new Point(64, 15);

                if (IsWalkableTile(location, entry.X, entry.Y))
                    return entry;

                // The entry point can be blocked on a farm with buildings; search a small band around
                // it before falling back to a full scan.
                for (int radius = 1; radius <= 8; radius++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                                continue;
                            int x = entry.X + dx;
                            int y = entry.Y + dy;
                            if (IsWalkableTile(location, x, y))
                                return new Point(x, y);
                        }
                    }
                }

                for (int x = 1; x < location.Map.Layers[0].LayerWidth - 1; x++)
                {
                    for (int y = 1; y < location.Map.Layers[0].LayerHeight - 1; y++)
                    {
                        if (IsWalkableTile(location, x, y))
                            return new Point(x, y);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Spawn] Finding a spawn point in '{location.NameOrUniqueName}' failed: {ex.Message}");
            }
            return new Point(64, 15);
        }

        /// <summary>Tile just outside the main farmhouse door, used as the farm's anchor point.</summary>
        static Point? GetFarmhouseDoor(GameLocation location)
        {
            try
            {
                foreach (var building in location.buildings)
                {
                    if (building.isCabin)
                        continue;
                    if (building.GetIndoors() is FarmHouse)
                    {
                        Point porch = building.getPorchStandingSpot();
                        return new Point(porch.X, porch.Y);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Spawn] Locating the farmhouse door failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>Whether a farmer can stand on this tile (no building, object or blocking tile).</summary>
        static bool IsWalkableTile(GameLocation location, int x, int y)
        {
            try
            {
                if (x < 1 || y < 1 || x >= location.Map.Layers[0].LayerWidth - 1 || y >= location.Map.Layers[0].LayerHeight - 1)
                    return false;
                if (!location.isTilePassable(new Location(x, y), Game1.viewport))
                    return false;
                if (location.Objects.ContainsKey(new Vector2(x, y)))
                    return false;
                if (location.getBuildingAt(new Vector2(x, y)) != null)
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures a farmhand owns a cabin before the client is told where it is. Vanilla does this in
        /// <c>GameServer.checkFarmhandRequest</c>; without it the farmhand keeps the default
        /// <c>homeLocation = "FarmHouse"</c> and wakes up inside the host's bedroom.
        /// </summary>
        static void PrepareFarmhandForJoin(Farmer farmer)
        {
            bool assigned = AssignFarmhandCabin(farmer);
            Console.WriteLine($"[FarmhandHome] Join: {farmer.Name} ({farmer.UniqueMultiplayerID}) home='{farmer.homeLocation.Value}', cabin={(assigned ? "ok" : "MISSING")}.");

            // Make sure the home actually has a bed, otherwise the wake-up spot degenerates to the
            // map's entry tile, which is where the "spawns in the corner" reports came from.
            if (Game1.getLocationFromName(farmer.homeLocation.Value) is FarmHouse home)
            {
                if (home.GetPlayerBed() == null)
                {
                    home.furniture.Add(new BedFurniture(BedFurniture.DEFAULT_BED_INDEX, new Vector2(9f, 8f)));
                    Console.WriteLine($"[FarmhandHome] Added a fallback bed to '{home.NameOrUniqueName}' for {farmer.Name}.");
                }

                if (string.IsNullOrEmpty(farmer.lastSleepLocation.Value))
                    farmer.lastSleepLocation.Value = home.NameOrUniqueName;

                // lastSleepPoint must be a real tile. BedFurniture.ApplyWakeUpPosition() takes the
                // "slept here" branch whenever lastSleepLocation is a valid location with a bed, and
                // places the farmer at lastSleepPoint * 64 - a default (0,0) point therefore drops
                // the client at the map corner on every join. Point it at the bed.
                Point bedSpot = home.GetPlayerBedSpot();
                if (farmer.lastSleepPoint.Value != bedSpot)
                {
                    Console.WriteLine($"[FarmhandHome] {farmer.Name}: lastSleepPoint ({farmer.lastSleepPoint.Value.X},{farmer.lastSleepPoint.Value.Y}) -> bed ({bedSpot.X},{bedSpot.Y}).");
                    farmer.lastSleepPoint.Value = bedSpot;
                }

                // Put the farmhand in its own cabin. The spot is only usable when the farmer is
                // actually inside the home; a farmhand that still sits in the host's house (or at the
                // origin) makes the client spawn next to the host and, on maps where that tile is
                // blocked, in the corner. The previous check only tested for the origin and let
                // "inside FarmHouse at (9,9)" through.
                string where = farmer.currentLocation?.NameOrUniqueName ?? "";                bool inOwnHome = string.Equals(where, home.NameOrUniqueName, StringComparison.OrdinalIgnoreCase);
                int mapWidth = home.Map?.Layers[0].LayerWidth ?? 0;
                int mapHeight = home.Map?.Layers[0].LayerHeight ?? 0;
                bool outOfMap = mapWidth > 0 && (TileX(farmer) < 0 || TileY(farmer) < 0 || TileX(farmer) >= mapWidth || TileY(farmer) >= mapHeight);

                if (!inOwnHome || outOfMap || farmer.Position == Vector2.Zero)
                {
                    Point bed = home.GetPlayerBedSpot();
                    Console.WriteLine($"[FarmhandHome] {farmer.Name} was at '{where}' ({(int)(farmer.Position.X / 64)},{(int)(farmer.Position.Y / 64)}), "
                        + $"not in their cabin; placing them on the bed of '{home.NameOrUniqueName}' at ({bed.X},{bed.Y}).");
                    farmer.currentLocation = home;
                    farmer.Position = new Vector2(bed.X * 64f, bed.Y * 64f);
                }
            }
        }

        /// <summary>
        /// Pins each online farmhand's sleeping spot on the server-side copy just before the day roll.
        ///
        /// Vanilla restores the wake-up position from two places: NetWorldState.ResetFarmhandState()
        /// sends a farmhand back to their home cabin bed when lastSleepLocation is null, and
        /// BedFurniture.ApplyWakeUpPosition runs on the farmhand's own client. A stale value left over
        /// from an earlier night therefore makes the client wake up in a completely different
        /// building, so the value the roll sees must be the one from tonight.
        ///
        /// GameLocation.doSleep() sets these fields on the client, but that runs after the client
        /// reports sleep-ready, so the server may still hold the previous night's value when the roll
        /// starts. The farmhand is already standing on the bed at this point, so always overwrite with
        /// what we observe rather than only filling in an empty field.
        /// </summary>
        static void CaptureSleepPositions()
        {
            int recorded = 0;
            foreach (var entry in Game1.otherFarmers)
            {
                Farmer farmer = entry.Value;
                string location = farmer.currentLocation?.NameOrUniqueName ?? "";
                if (location.Length == 0)
                    continue;

                int tileX = TileX(farmer);
                int tileY = TileY(farmer);
                string previous = farmer.lastSleepLocation.Value ?? "";
                farmer.lastSleepLocation.Value = location;
                farmer.lastSleepPoint.Value = new Microsoft.Xna.Framework.Point(tileX, tileY);
                recorded++;

                if (previous != location)
                {
                    Console.WriteLine($"[WakeUp] {farmer.Name}: sleeping spot pinned to '{location}' ({tileX},{tileY}), was '{previous}'.");
                }
            }
            if (recorded > 0)
                Console.WriteLine($"[WakeUp] Pinned the sleeping spot for {recorded} farmhand(s).");
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

            if (hostSleepTriggered)
            {
                // Already voted. ServerReadyCheck only flips IsReady once every required farmer has
                // accepted the lock, so wait for that instead of rolling the day on the host's vote:
                // a farmhand that is still walking to bed would otherwise be caught mid-walk and wake
                // up wherever the overnight pass left it.
                if (ready < required - 1)
                {
                    hostSleepTriggered = false;
                    Console.WriteLine($"[HeadlessNewDay] A farmhand withdrew from the sleep check ({ready}/{required}); standing by.");
                    return;
                }
                if (!Game1.netReady.IsReady("sleep"))
                    return;

                hostSleepTriggered = false;
                Console.WriteLine($"[HeadlessNewDay] Sleep check locked ({ready}/{required}); starting NewDay (time {Game1.timeOfDay}, day {Game1.dayOfMonth})...");
                // Capture before the day roll: the overnight pass re-homes farmhands and moves them,
                // so this is the last point where the spot they chose is still intact.
                CaptureSleepPositions();
                try
                {
                    Game1.NewDay(0f);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HeadlessNewDay] Game1.NewDay failed: {ex}");
                }
                return;
            }

            // Same test DedicatedServer.CheckOthersReady() uses: every farmer except the host has
            // voted. Comparing against clientConnections.Count instead also counted peers that were
            // still handshaking, so the host could be put to bed while a farmhand was not.
            bool othersReady = ready > 0 && required > 0 && ready >= required - 1;
            if (!othersReady || Game1.netReady.IsReady("sleep"))
                return;

            if (ready != lastLogReady || required != lastLogRequired)
            {
                lastLogReady = ready;
                lastLogRequired = required;
                Console.WriteLine($"[HeadlessNewDay] {ready}/{required} farmer(s) in bed; putting the host to bed.");
            }

            // The headless server never runs the render-loop warp a real dedicated host relies on to
            // walk itself home and join the night check, so reproduce DedicatedServer.HostSleepInBed():
            // place the invisible host in its own farmhouse bed and answer the sleep prompt, which
            // makes it sleep-ready.
            Farmer host = Game1.player;
            host.isInBed.Value = true;
            host.timeWentToBed.Value = (int)Game1.timeOfDay;
            host.Halt();
            try
            {
                Game1.netReady.SetLocalReady("sleep", true);
                Game1.netReady.SetLocalReady("ready_for_save", true);
                Game1.netReady.SetLocalReady("wakeup", true);
                hostSleepTriggered = true;
                Console.WriteLine($"[HeadlessNewDay] Host marked sleep ready ({ready}/{required}).");
            }
            catch (Exception ex)
            {
                hostSleepTriggered = false;
                Console.WriteLine($"[HeadlessNewDay] Host sleep setup failed: {ex}");
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
            uint dayBeforeRoll = Game1.stats.DaysPlayed;
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

                    // The overnight coroutine rebuilds farmhouse interiors (building upgrades,
                    // cellar layout). Snapshot right here so a house that loses furniture or a
                    // farm that loses its objects is attributable to the day roll itself.
                    LogWorldSnapshot("after-newday", force: true);

                    // Hand every farmhand a fresh full copy of the farm and of its houses. Location
                    // deltas are incremental, so anything a delta garbled on the client side would
                    // otherwise persist until the player reconnected - that is how the farm lost its
                    // crops and buildings and the main house lost half of its furniture after an
                    // overnight. Do this while the clients are still inside the day-roll handshake.
                    ResyncLocationsToClients("overnight");

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
                                // The headless server skips SaveGameMenu.update(), so it runs both
                                // halves of the save handshake itself. Wait until every farmhand has
                                // reached the save menu (that is what turns readyForSave() true) before
                                // snapshotting the world, then broadcast the "saved" variable.
                                WaitForNewDayStage(() => Game1.newDaySync.readyForSave(), "ready_for_save", 60000);
                                // This is where the night's progress is persisted: after the whole
                                // overnight sequence (crops, farmhands, shipping) is done and before
                                // the new day is announced to clients.
                                SaveNightlyProgress();
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
                                WaitForNewDayStage(() => Game1.newDaySync.readyForFinish(), "wakeup", 60000);
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
                    bool dayAdvanced = Game1.stats.DaysPlayed != dayBeforeRoll;
                    Game1.newDay = false;
                    headlessNewDayProcess = null;
                    headlessNewDayActive = false;
                    headlessNewDayThread = null;
                    if (dayAdvanced)
                    {
                        try { Game1.timeOfDay = 600; Game1.netWorldState?.Value?.UpdateFromGame1(); } catch { }
                        Console.WriteLine($"[HeadlessNewDay] newDay cleared (day {Game1.dayOfMonth}, time reset to {Game1.timeOfDay}).");
                    }
                    else
                    {
                        // The coroutine threw before the calendar moved. Forcing the clock back to
                        // 06:00 here would swallow the night and leave every farmhand unable to
                        // retry it, so keep the current time and let the next attempt start over.
                        Console.WriteLine("[HeadlessNewDay][warn] Overnight coroutine ended without advancing the day; keeping the current time so the night can be retried.");
                    }
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

        /// <summary>
        /// Pumps one of the post-night ReadyChecks until every farmhand has answered it.
        ///
        /// A real host walks through these from SaveGameMenu.update() and
        /// Game1.PollForEndOfNewDaySync(), which both call readyForSave()/readyForFinish() in a loop.
        /// The headless server has neither that menu nor a render loop, so it has to poll the checks
        /// itself; calling the poll method is also what pumps the network (it runs team.Update() and
        /// Multiplayer.UpdateLate/UpdateEarly), so this loop keeps the answers flowing.
        /// </summary>
        static bool WaitForNewDayStage(Func<bool> poll, string stage, int timeoutMilliseconds)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                try
                {
                    if (poll())
                        return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HeadlessNewDay] The {stage} ready check failed: {ex.Message}");
                    return false;
                }
                Thread.Sleep(16);
            }
            Console.WriteLine($"[HeadlessNewDay] Timed out after {timeoutMilliseconds} ms waiting for the {stage} ready check; continuing without it.");
            return false;
        }

        /// <summary>
        /// Directory holding the saved farmhand XML files. A relative configured path
        /// resolves against the executable directory.
        /// </summary>
        private static string savedFarmhandsPath
        {
            get
            {
                string configured = ServerConfig.Current.Paths.SaveDirectory;
                return Path.IsPathRooted(configured)
                    ? configured
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
            }
        }
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
                    if (!long.TryParse(Path.GetFileNameWithoutExtension(file), out long fileId))
                    {
                        Console.WriteLine($"[Save][WARN] Skipping {Path.GetFileName(file)}: the file name is not a farmhand ID.");
                        continue;
                    }

                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                        var farmer = serializer.Deserialize(stream) as Farmer;
                        if (farmer == null)
                        {
                            Console.WriteLine($"[Save][ERROR] {Path.GetFileName(file)} did not deserialize to a Farmer.");
                            continue;
                        }

                        // The serializer does not round-trip Farmer.UniqueMultiplayerID, so a restored
                        // farmhand comes back with ID 0 and used to be dropped here without a word.
                        // The file name (<id>.xml) is the authoritative identity.
                        if (farmer.UniqueMultiplayerID != fileId)
                        {
                            Console.WriteLine($"[Save] Restored farmhand ID {fileId} from the file name (serialized value was {farmer.UniqueMultiplayerID}).");
                            farmer.UniqueMultiplayerID = fileId;
                        }

                        if (farmer.UniqueMultiplayerID != 99999999L && farmer.UniqueMultiplayerID != 0)
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
                    Console.WriteLine($"[Save][ERROR] Cannot load farmhand from {file}: {ex}");
                }
            }
            Console.WriteLine($"[Save] Loaded {list.Count} saved farmhand(s) from {savedFarmhandsPath}.");
            return list;
        }

        /// <summary>
        /// The parts of the world that must survive a restart for the saved farmhands to stay
        /// meaningful. Cabin interiors get a fresh GUID every time the world is built, so without
        /// this every restored farmhand points at a location that no longer exists and cannot sleep.
        /// </summary>
        sealed class SavedWorld
        {
            public int Version { get; set; } = 1;
            public ulong GameId { get; set; }
            public int Year { get; set; }
            public string Season { get; set; } = "spring";
            public int DayOfMonth { get; set; }
            public int FarmType { get; set; }
            public List<string> CabinNames { get; set; } = new();
        }

        static string worldStatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "world.json");

        /// <summary>
        /// Writes the world core (calendar, game id, cabin identities) at the nightly save point.
        /// </summary>
        static bool SaveWorldCore()
        {
            try
            {
                var farm = Game1.getFarm();
                var world = new SavedWorld
                {
                    GameId = Game1.uniqueIDForThisGame,
                    Year = Game1.year,
                    Season = Game1.currentSeason ?? "spring",
                    DayOfMonth = Game1.dayOfMonth,
                    FarmType = Game1.whichFarm,
                    CabinNames = farm.buildings
                        .Where(b => b.isCabin)
                        .Select(b => b.GetIndoors()?.NameOrUniqueName ?? "")
                        .ToList()
                };

                string json = JsonSerializer.Serialize(world, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = worldStatePath + ".tmp";
                File.WriteAllText(tempPath, json);
                try
                {
                    File.Move(tempPath, worldStatePath, true);
                }
                catch
                {
                    File.WriteAllText(worldStatePath, json);
                    TryDeleteFile(tempPath);
                }

                var info = new FileInfo(worldStatePath);
                if (!info.Exists || info.Length == 0)
                {
                    Console.WriteLine($"[World][ERROR] {worldStatePath} is missing or empty after saving.");
                    return false;
                }
                Console.WriteLine($"[World] Saved world core: year {world.Year} {world.Season} {world.DayOfMonth}, {world.CabinNames.Count} cabin(s), {info.Length} bytes.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][ERROR] Saving world core failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// World folder chosen at startup (<c>new</c> for a fresh world). Set once in Main, read by
        /// the world loader and by the <c>saves</c> command, which reports how to change it.
        /// </summary>
        static string SelectedWorldFolder = "";

        /// <summary>
        /// Finds the save to load: the world folder chosen at startup
        /// (<c>&lt;saves&gt;/&lt;folder&gt;/&lt;folder&gt;</c>), or the newest save this server wrote
        /// when no folder was chosen. Returns null for a new world.
        /// </summary>
        static string? FindNativeSaveFile()
        {
            try
            {
                if (SelectedWorldFolder.Equals(SaveSelection.NewWorldChoice, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[World] Starting a new world as chosen at startup.");
                    return null;
                }

                string root = StardewValley.Program.GetSavesFolder();
                if (!Directory.Exists(root))
                    return null;

                if (!string.IsNullOrWhiteSpace(SelectedWorldFolder))
                {
                    string chosen = Path.Combine(root, SelectedWorldFolder, SelectedWorldFolder);
                    if (File.Exists(chosen))
                        return chosen;

                    Console.WriteLine($"[World][ERROR] The chosen world '{SelectedWorldFolder}' has no data file at {chosen}.");
                    return null;
                }

                // No explicit choice (older config): fall back to the newest save the server wrote.
                string prefix = nativeSaveName + "_";
                string? best = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (string dir in Directory.GetDirectories(root, prefix + "*"))
                {
                    string folderName = Path.GetFileName(dir);
                    string candidate = Path.Combine(dir, folderName);
                    if (!File.Exists(candidate))
                        continue;

                    DateTime written = File.GetLastWriteTimeUtc(candidate);
                    if (written > bestTime)
                    {
                        bestTime = written;
                        best = candidate;
                    }
                }
                return best;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][ERROR] Looking for a native save failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a native save while skipping its <c>&lt;options&gt;</c> block.
        /// The headless host builds <see cref="Options"/> with
        /// <c>FormatterServices.GetUninitializedObject</c>, so none of its field initializers (input
        /// button arrays and friends) ever ran. Deserializing the block the game wrote therefore dies
        /// with a NullReferenceException deep inside the serializer, and the server has no use for
        /// the player's graphics/input settings anyway.
        /// </summary>
        static SaveGame ReadNativeSaveWithoutOptions(string saveFile)
        {
            byte[] bytes = File.ReadAllBytes(saveFile);
            string xml = System.Text.Encoding.UTF8.GetString(bytes);

            // <options> cannot be deserialized in the headless host at all: several of its
            // non-[XmlIgnore] properties (hardwareCursor, allowStowing, SnappyMenus, ...) read
            // Game1.game1 / Game1.input in their getters, and the server has neither. The server
            // does not use the player's graphics or input settings, so drop the block outright.
            const string open = "<options>";
            const string close = "</options>";
            int start = xml.IndexOf(open, StringComparison.Ordinal);
            if (start >= 0)
            {
                int end = xml.IndexOf(close, start, StringComparison.Ordinal);
                if (end > start)
                {
                    xml = xml.Substring(0, start) + xml.Substring(end + close.Length);
                    Console.WriteLine("[World] Removed the saved <options> block (graphics/input settings, unused by the server).");
                }
            }

            byte[] patched = System.Text.Encoding.UTF8.GetBytes(xml);
            using var stream = new MemoryStream(patched, writable: false);
            return SaveSerializer.Deserialize<SaveGame>(stream)
                ?? throw new InvalidDataException("The save deserialized to null.");
        }

        /// <summary>
        /// Probes the <c>&lt;options&gt;</c> block on its own. The save records the full Options
        /// object the client wrote, while the headless host builds Options without its constructor,
        /// so its input arrays are null; whether that is what breaks the save read is far cheaper to
        /// test on the block alone than on the whole 3 MB document.
        /// </summary>
        static bool ProbeOptionsBlock(string saveFile)
        {
            try
            {
                string xml = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(saveFile));
                const string open = "<options>";
                const string close = "</options>";
                int start = xml.IndexOf(open, StringComparison.Ordinal);
                if (start < 0)
                {
                    Console.WriteLine("[World][probe] No <options> block in the save.");
                    return true;
                }
                int end = xml.IndexOf(close, start, StringComparison.Ordinal);
                string block = xml.Substring(start, end - start + close.Length);
                string wrapped = "<?xml version=\"1.0\" encoding=\"utf-8\"?><SaveGame xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">" + block + "</SaveGame>";

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wrapped), writable: false);
                SaveSerializer.Deserialize<SaveGame>(stream);
                Console.WriteLine("[World][probe] <options> alone deserializes fine.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][probe] <options> alone FAILS: {ex.GetType().Name}: {ex.Message}"
                    + (ex.InnerException != null ? $" -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : ""));

                // Probe an empty block as well: if the empty form also fails, the serializer itself
                // is broken for this type (a NullReferenceException thrown while loading Options
                // taints every later deserialization), not the saved values.
                try
                {
                    string emptyWrapped = "<?xml version=\"1.0\" encoding=\"utf-8\"?><SaveGame xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><options /></SaveGame>";
                    using var emptyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(emptyWrapped), writable: false);
                    SaveSerializer.Deserialize<SaveGame>(emptyStream);
                    Console.WriteLine("[World][probe] empty <options /> deserializes fine.");
                }
                catch (Exception emptyEx)
                {
                    Console.WriteLine($"[World][probe] empty <options /> ALSO FAILS: {emptyEx.GetType().Name}: {emptyEx.Message}"
                        + (emptyEx.InnerException != null ? $" -> {emptyEx.InnerException.GetType().Name}: {emptyEx.InnerException.Message}" : ""));
                    DumpOptionsFields();
                    ProbeSaveElements();
                }
                return false;
            }
        }

        /// <summary>
        /// Deserializes each top-level SaveGame element on its own to find which one the headless
        /// host cannot read. A NullReferenceException from XmlSerializer carries no element context,
        /// so isolating elements is the only reliable way to name it.
        /// </summary>
        static void ProbeSaveElements()
        {
            const string head = "<?xml version=\"1.0\" encoding=\"utf-8\"?><SaveGame xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">";
            var cases = new (string name, string body)[]
            {
                ("empty", ""),
                ("options", "<options />"),
                ("bundleData", "<bundleData />"),
                ("latestID", "<latestID>5</latestID>"),
                ("player", "<player />"),
                ("farmhands", "<farmhands />"),
                ("locations", "<locations />"),
                ("farmerFriendships", "<farmerFriendships />"),
                ("mine_permanentMineChanges", "<mine_permanentMineChanges />"),
                ("splitscreenOptions", "<splitscreenOptions />"),
                ("junimoKartLeaderboards", "<junimoKartLeaderboards />"),
                ("raccoonBundles", "<raccoonBundles />"),
            };
            foreach (var (name, body) in cases)
            {
                try
                {
                    using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(head + body + "</SaveGame>"), writable: false);
                    SaveGame.serializer.Deserialize(ms);
                    Console.WriteLine($"[World][probe] element {name}: OK");
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException;
                    Console.WriteLine($"[World][probe] element {name}: FAIL {ex.GetType().Name}: {ex.Message}"
                        + (inner != null ? $" -> {inner.GetType().Name}: {inner.Message}" : ""));
                }
            }
        }

        /// <summary>
        /// Re-creates the <see cref="InputButton"/> arrays that <see cref="Options"/> initializes in
        /// its field declarations. The headless host skips that constructor (it needs a graphics
        /// device), and an Options instance with null input arrays makes every later
        /// XmlSerializer run throw a NullReferenceException - including while reading a save.
        /// The shapes come from the type's initializers; when reflection cannot see one, a single
        /// unbound button is used, which is enough for serialization and for the run key.
        /// </summary>
        static void InitializeOptionsInputArrays(Options options)
        {
            // The array lengths are fixed by Options' field initializers; they must match so a
            // serialized <options> block round-trips to the same shape.
            var lengths = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["actionButton"] = 2, ["cancelButton"] = 1, ["useToolButton"] = 2,
                ["moveUpButton"] = 1, ["moveRightButton"] = 1, ["moveDownButton"] = 1, ["moveLeftButton"] = 1,
                ["menuButton"] = 2, ["runButton"] = 1, ["tmpKeyToReplace"] = 1, ["chatButton"] = 2,
                ["mapButton"] = 1, ["journalButton"] = 1, ["toolbarSwap"] = 1, ["emoteButton"] = 1,
            };
            for (int i = 1; i <= 12; i++)
                lengths["inventorySlot" + i] = 1;

            var fields = typeof(Options).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(InputButton[]));
            int created = 0;
            foreach (var field in fields)
            {
                if (field.GetValue(options) != null)
                    continue;

                int length = lengths.TryGetValue(field.Name, out int declared) ? declared : 1;
                field.SetValue(options, Enumerable.Range(0, length).Select(_ => new InputButton(Keys.None)).ToArray());
                created++;
            }
            Console.WriteLine($"[Init] Restored {created} Options input array(s) so Options can be serialized.");
        }

        /// <summary>Reports which Options fields the headless host leaves null (constructor skipped).</summary>
        static void DumpOptionsFields()
        {
            try
            {
                if (Game1.options == null)
                {
                    Console.WriteLine("[World][probe] Game1.options is null.");
                    return;
                }
                var nulls = typeof(Options)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => !f.FieldType.IsValueType && !f.FieldType.IsPrimitive && f.GetValue(Game1.options) == null)
                    .Select(f => f.FieldType.Name + " " + f.Name)
                    .Take(12)
                    .ToList();
                Console.WriteLine($"[World][probe] Options null reference-typed fields ({nulls.Count} shown): {string.Join(", ", nulls)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][probe] DumpOptionsFields failed: {ex.Message}");
            }
        }

        /// <summary>Sets an internal static field on Game1 (the save-fix flags are not public).</summary>
        static void SetInternalStaticField(string name, bool value)
        {
            try
            {
                var field = typeof(Game1).GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                    field.SetValue(null, value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][WARN] Could not set Game1.{name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the world from the native save the server wrote last time it stopped. This is
        /// what makes placed objects, tilled soil, crops, buildings and farmhand progress survive a
        /// restart; the previous behaviour rebuilt a fresh world every start, so everything the
        /// players did was silently discarded while their items stayed consumed.
        /// The game's own loader is used for the parts that carry the world
        /// (<see cref="SaveGame.loadDataToLocations"/> plus the calendar and ids), because it already
        /// handles save migrations and instanced buildings.
        /// </summary>
        static bool LoadNativeGame()
        {
            string? saveFile = FindNativeSaveFile();
            if (saveFile == null)
            {
                Console.WriteLine("[World] No native save found; starting a fresh world.");
                return false;
            }

            Console.WriteLine($"[World] Loading native save {saveFile} ...");
            DateTime startedAt = DateTime.UtcNow;
            ProbeOptionsBlock(saveFile);
            try
            {
                // Read through the game's serializer directly instead of TryReadSaveFile, so a
                // failure keeps its inner exception (the outer "error in XML document" message alone
                // does not say which element failed).
                SaveGame save;
                try
                {
                    save = ReadNativeSaveWithoutOptions(saveFile);
                }
                catch (Exception readEx)
                {
                    Console.WriteLine($"[World][ERROR] Native save could not be read: {readEx}");
                    var inner = readEx.InnerException;
                    int depth = 0;
                    while (inner != null && depth++ < 5)
                    {
                        Console.WriteLine($"[World][ERROR]   caused by: {inner.GetType().Name}: {inner.Message}");
                        inner = inner.InnerException;
                    }
                    return false;
                }

                SaveGame.loaded = save;

                // Calendar and identity first: everything below keys off them.
                Game1.uniqueIDForThisGame = save.uniqueIDForThisGame;
                Game1.SetSaveName(Path.GetFileNameWithoutExtension(saveFile).Split('_')[0]);
                Game1.year = save.year;
                Game1.currentSeason = save.currentSeason;
                Game1.dayOfMonth = save.dayOfMonth;
                if (Enum.TryParse(typeof(StardewValley.SaveMigrations.SaveFixes), save.lastAppliedSaveFix.ToString(), out object? fix) && fix != null)
                    typeof(Game1).GetField("lastAppliedSaveFix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        ?.SetValue(null, fix);
                SetInternalStaticField("hasApplied1_3_UpdateChanges", save.hasApplied1_3_UpdateChanges);
                SetInternalStaticField("hasApplied1_4_UpdateChanges", save.hasApplied1_4_UpdateChanges);
                Game1.weatherForTomorrow = save.weatherForTomorrow;
                Game1.isRaining = save.isRaining;
                Game1.isLightning = save.isLightning;
                Game1.isSnowing = save.isSnowing;
                if (save.highestPlayerLimit >= 0)
                    Game1.netWorldState.Value.HighestPlayerLimit = save.highestPlayerLimit;
                SaveGame.LoadFarmType();

                // Player statistics carry DaysPlayed, which drives day-based logic on the client
                // (disconnect handling, save fixes, seasonal checks). Leaving it at the fresh value
                // of 0 made the server and the clients disagree about which day it is, so every
                // "did I disconnect today?" style check misfired.
                // Game1.stats is a read-only alias of Game1.player.stats, so restore through the
                // host farmer's field.
                if (save.player?.stats != null && Game1.player != null)
                {
                    Game1.player.stats = save.player.stats;
                    Console.WriteLine($"[World] Restored player stats (DaysPlayed={Game1.stats.DaysPlayed}).");
                }

                Game1.netWorldState.Value.UpdateFromGame1();

                // Locations: objects, terrain features (tilled soil and crops), buildings, furniture,
                // resource clumps and animals are all merged onto the freshly built location catalog.
                SaveGame.loadDataToLocations(save.locations);

                // Farmhands travel inside the native save. Mirror what LoadSavedFarmhands does for
                // the per-farmhand files so restored characters stay selectable.
                int restoredFarmhands = 0;
                if (save.farmhands != null)
                {
                    foreach (Farmer farmhand in save.farmhands)
                    {
                        if (farmhand == null || farmhand.UniqueMultiplayerID is 0 or 99999999L)
                            continue;
                        // The debris self-test registers a throwaway farmer; older saves captured it.
                        if (farmhand.UniqueMultiplayerID == 88888888L)
                        {
                            Console.WriteLine("[World] Dropped the debris self-test farmer from the save.");
                            continue;
                        }
                        Game1.netWorldState.Value.farmhandData[farmhand.UniqueMultiplayerID] = farmhand;
                        savedFarmhandCatalog[farmhand.UniqueMultiplayerID] = farmhand;
                        savedFarmerIds.Add(farmhand.UniqueMultiplayerID);
                        restoredFarmhands++;
                    }
                }

                if (save.shippingBin != null)
                {
                    var bin = Game1.getFarm().getShippingBin(Game1.player);
                    bin.Clear();
                    bin.AddRange(save.shippingBin);
                }

                Game1.netWorldState.Value.UpdateFromGame1();
                Game1.flushLocationLookup();

                int farm = Game1.getFarm()?.objects.Count() ?? 0;
                int terrain = Game1.getFarm()?.terrainFeatures.Count() ?? 0;
                int crops = Game1.getFarm()?.terrainFeatures.Values.OfType<StardewValley.TerrainFeatures.HoeDirt>()
                    .Count(h => h.crop != null) ?? 0;
                int elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                Console.WriteLine($"[World] Native save restored: year {Game1.year} {Game1.currentSeason} {Game1.dayOfMonth}, "
                    + $"farm objects={farm}, terrain={terrain}, crops={crops}, farmhands={restoredFarmhands} ({elapsedMs} ms).");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][ERROR] Restoring the native save failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Applies the saved world core to the freshly constructed world. Must run after the default
        /// buildings exist and before the farmhands are loaded, so their saved homes resolve.
        /// </summary>
        static void RestoreWorldCore()
        {
            try
            {
                if (!File.Exists(worldStatePath))
                {
                    Console.WriteLine("[World] No saved world core yet; starting a fresh world.");
                    return;
                }

                var world = JsonSerializer.Deserialize<SavedWorld>(File.ReadAllText(worldStatePath));
                if (world == null)
                {
                    Console.WriteLine("[World][ERROR] world.json could not be parsed; starting a fresh world.");
                    return;
                }
                if (world.Version != 1)
                {
                    Console.WriteLine($"[World][WARN] Unsupported world.json version {world.Version}; ignoring it.");
                    return;
                }
                if (world.FarmType != Game1.whichFarm)
                {
                    Console.WriteLine($"[World][WARN] Saved world is farm type {world.FarmType} but config asks for {Game1.whichFarm}; ignoring the saved world.");
                    return;
                }

                Game1.year = Math.Max(1, world.Year);
                Game1.currentSeason = string.IsNullOrEmpty(world.Season) ? "spring" : world.Season;
                Game1.dayOfMonth = Math.Clamp(world.DayOfMonth, 1, 28);
                if (world.GameId != 0)
                    Game1.uniqueIDForThisGame = world.GameId;

                var cabins = Game1.getFarm().buildings.Where(b => b.isCabin).ToList();
                int renamed = 0;
                for (int i = 0; i < cabins.Count && i < world.CabinNames.Count; i++)
                {
                    string savedName = world.CabinNames[i];
                    var indoors = cabins[i].GetIndoors();
                    if (indoors == null || string.IsNullOrEmpty(savedName))
                        continue;
                    if (indoors.NameOrUniqueName == savedName)
                        continue;

                    indoors.name.Value = savedName;
                    renamed++;
                }
                if (renamed > 0)
                    Game1.flushLocationLookup();

                Game1.netWorldState.Value.UpdateFromGame1();
                Console.WriteLine($"[World] Restored world core: year {Game1.year} {Game1.currentSeason} {Game1.dayOfMonth}, {renamed} cabin(s) remapped to their saved names.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[World][ERROR] Restoring world core failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the game state at the end of the night. This mirrors where vanilla writes the
        /// save (SaveGameMenu.update(), between readyForSave and flagSaved) and is the authoritative
        /// save point: everything the players did during the day is flushed here, once per in-game
        /// day, before the new day is announced to clients.
        /// </summary>
        static void SaveNightlyProgress()
        {
            Console.WriteLine($"[Save] Nightly save starting (year {Game1.year}, season {Game1.currentSeason}, day {Game1.dayOfMonth})...");
            bool nativeOk = SaveNativeGame();
            bool worldOk = SaveWorldCore();
            int failed = SaveAllActiveFarmhands() + SaveOfflineFarmhands();
            if (failed == 0 && worldOk && nativeOk)
                Console.WriteLine("[Save] Nightly save complete.");
            else
                Console.WriteLine($"[Save][ERROR] Nightly save finished with {failed} farmhand failure(s), "
                    + $"world core {(worldOk ? "ok" : "FAILED")}, native save {(nativeOk ? "ok" : "FAILED")}; progress may be lost.");
        }

        // ------------------------ Native save ------------------------

        /// <summary>
        /// Farm name in the form the game uses for save folder names.
        /// </summary>
        static string nativeSaveName
        {
            get
            {
                string? name = Game1.player?.farmName?.Value;
                return SaveGame.FilterFileName(string.IsNullOrWhiteSpace(name) ? "HeadlessFarm" : name);
            }
        }

        /// <summary>
        /// Absolute path of the save file the game itself would write:
        /// <c>&lt;saves&gt;/&lt;name&gt;_&lt;gameId&gt;/&lt;name&gt;_&lt;gameId&gt;</c>.
        /// Folder and file share one name, exactly like a save created in game.
        /// </summary>
        static string nativeSaveFile
        {
            get
            {
                string fileName = nativeSaveName + "_" + Game1.uniqueIDForThisGame;
                return Path.Combine(StardewValley.Program.GetSavesFolder(), fileName, fileName);
            }
        }

        /// <summary>
        /// Writes the whole world with the game's own save pipeline.
        /// <see cref="SaveGame.getSaveEnumerator"/> packs player, farmhands, every location and the
        /// world state into a single save file, so the result is a real Stardew save rather than a
        /// server-specific format. The enumerator is driven synchronously here instead of going
        /// through <see cref="SaveGame.Save"/>, which offloads the work to a task started by the mod
        /// hooks the headless host only partially provides.
        /// </summary>
        static bool SaveNativeGame()
        {
            string fileName = nativeSaveName + "_" + Game1.uniqueIDForThisGame;
            string folderPath = Path.Combine(StardewValley.Program.GetSavesFolder(), fileName);
            string filePath = Path.Combine(folderPath, fileName);

            // Pin the save name: GetSaveGameName() otherwise appends 2/3/... whenever a folder with
            // this name already exists, and a save whose name drifts is a save that cannot be found
            // again by name on the next start.
            try
            {
                Game1.SetSaveName(nativeSaveName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][WARN] Could not pin the save name to '{nativeSaveName}': {ex.Message}");
            }

            DateTime startedAt = DateTime.UtcNow;
            try
            {
                IEnumerator<int> save = SaveGame.getSaveEnumerator();
                while (save.MoveNext())
                {
                    // The enumerator reports progress; the headless host has nowhere to show it.
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][ERROR] Native save failed: {ex}");
                return false;
            }
            finally
            {
                // getSaveEnumerator sets this flag and normally SaveGame.Save clears it.
                SaveGame.IsProcessing = false;
            }

            int elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length == 0)
                {
                    Console.WriteLine($"[Save][ERROR] Native save produced no file at {filePath}.");
                    return false;
                }

                string infoPath = Path.Combine(folderPath, "SaveGameInfo");
                if (!File.Exists(infoPath))
                    Console.WriteLine($"[Save][WARN] Native save has no SaveGameInfo beside {filePath}; the save list will not show it.");

                Console.WriteLine($"[Save] Native save written: {filePath} ({info.Length} bytes, {elapsedMs} ms).");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][ERROR] Cannot verify native save {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Persists farmhands that are in the catalog but offline. Their data does not change while
        /// they are away, but the nightly save keeps every known farmhand in sync with the day roll.
        /// </summary>
        static int SaveOfflineFarmhands()
        {
            int failed = 0;
            foreach (var entry in savedFarmhandCatalog)
            {
                if (Game1.otherFarmers.ContainsKey(entry.Key))
                    continue;
                if (!SaveFarmhand(entry.Value))
                    failed++;
            }
            return failed;
        }

        /// <summary>
        /// Persists one farmhand to <c>saved_farmhands/&lt;id&gt;.xml</c>. Returns true only when the
        /// file is on disk and verifiably non-empty: callers must never mark a farmhand as persisted
        /// on the strength of "the write was attempted".
        /// </summary>
        static bool SaveFarmhand(Farmer farmer)
        {
            try
            {
                Directory.CreateDirectory(savedFarmhandsPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][ERROR] Farmhand {farmer.Name}: cannot create {savedFarmhandsPath}: {ex.Message}");
                return false;
            }

            string filePath = Path.Combine(savedFarmhandsPath, $"{farmer.UniqueMultiplayerID}.xml");
            string tempPath = filePath + ".tmp";

            // Serialize beside the target first, so an interrupted write cannot truncate an
            // existing good save.
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                    serializer.Serialize(stream, farmer);
                    stream.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][ERROR] Farmhand {farmer.Name} ({farmer.UniqueMultiplayerID}): writing {tempPath} failed: {ex.Message}");
                TryDeleteFile(tempPath);
                return false;
            }

            // Publish the temp file. Some environments (restrictive ACLs, sandboxes, antivirus
            // holding a handle) deny rename/replace even where plain writes succeed; in that case
            // write through to the target directly: losing atomicity beats losing the save.
            try
            {
                File.Move(tempPath, filePath, true);
            }
            catch (Exception moveEx)
            {
                Console.WriteLine($"[Save][WARN] Farmhand {farmer.Name}: cannot replace {filePath} ({moveEx.Message}); writing in place.");
                try
                {
                    File.WriteAllBytes(filePath, File.ReadAllBytes(tempPath));
                    TryDeleteFile(tempPath);
                }
                catch (Exception directEx)
                {
                    Console.WriteLine($"[Save][ERROR] Farmhand {farmer.Name}: writing {filePath} in place failed: {directEx.Message}");
                    TryDeleteFile(tempPath);
                    return false;
                }
            }

            // Report what is actually on disk rather than what we intended to write.
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length == 0)
                {
                    Console.WriteLine($"[Save][ERROR] Farmhand {farmer.Name}: {filePath} is missing or empty after saving.");
                    return false;
                }
                Console.WriteLine($"[Save] Farmhand {farmer.Name} ({farmer.UniqueMultiplayerID}) saved to {filePath} ({info.Length} bytes)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][ERROR] Farmhand {farmer.Name}: cannot verify {filePath}: {ex.Message}");
                return false;
            }
        }

        static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Proves the save directory is usable before anyone connects, so a server that cannot
        /// persist farmhands says so at startup instead of silently dropping progress later.
        /// </summary>
        static void EnsureSaveDirectoryWritable()
        {
            string path = savedFarmhandsPath;
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".write-probe");
                File.WriteAllText(probe, "ok");
                string roundTrip = File.ReadAllText(probe);
                TryDeleteFile(probe);
                if (roundTrip != "ok")
                    throw new IOException("write probe did not read back its own content");
                Console.WriteLine($"[Save] Farmhand directory ready: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Save][ERROR] Farmhand directory is NOT writable: {path}");
                Console.WriteLine($"[Save][ERROR]   {ex.Message}");
                Console.WriteLine("[Save][ERROR] Farmhand progress will be lost unless Paths.SaveDirectory in config.json is fixed.");
            }
        }

        /// <summary>
        /// Persists every online farmhand and logs a single summary line. Returns the number of
        /// farmhands that failed, so the caller can tell a clean disconnect from a lost save.
        /// </summary>
        static int SaveAllActiveFarmhands()
        {
            int saved = 0;
            var failed = new List<string>();
            foreach (var rootKvp in Game1.otherFarmers.Roots)
            {
                var farmer = rootKvp.Value.Value;
                if (farmer == null || farmer.UniqueMultiplayerID == 99999999L || farmer.UniqueMultiplayerID == 0 || !farmer.isCustomized.Value)
                    continue;

                if (SaveFarmhand(farmer))
                    saved++;
                else
                    failed.Add($"{farmer.Name}({farmer.UniqueMultiplayerID})");
            }

            if (failed.Count == 0)
                Console.WriteLine($"[Save] Farmhand save finished: {saved} written, 0 failed.");
            else
                Console.WriteLine($"[Save][ERROR] Farmhand save finished: {saved} written, {failed.Count} FAILED: {string.Join(", ", failed)}");
            return failed.Count;
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
