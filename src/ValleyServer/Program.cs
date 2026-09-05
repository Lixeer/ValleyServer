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
        static Dictionary<long, NetConnection> clientConnections = new Dictionary<long, NetConnection>();
        static string? actualProtocolVersion = null;
        static long headlessClockAccumulatorMs = 0;
        static long headlessSimulationTimeMs = 0;
        static bool discardNextHeadlessClockElapsed = false;
        static readonly HashSet<long> serverModifiedFarmerIds = new HashSet<long>();
        private static readonly ConcurrentQueue<IncomingMessage> deferredOvernightMessages = new();
        static readonly FieldInfo gamePlayerField = typeof(Game1).GetField("_player", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(Game1).FullName, "_player");
        const int HeadlessMillisecondsPerTenMinutes = 1000;

        internal static void ProcessDeferredOvernightMessages()
        {
            Multiplayer? multiplayer = typeof(Game1).GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) as Multiplayer;
            int processed = 0;
            while (deferredOvernightMessages.TryDequeue(out IncomingMessage? message))
            {
                try
                {
                    // Detect the farmhand's barrier echo for removeItemsFromWorld. The master's
                    // _newDayAfterFade only sends the "farmEvent" synchronizer variable when
                    // stats.DaysPlayed > 1, but the client unconditionally waits for it before
                    // the "mail" barrier. On the first transition the master is DaysPlayed == 1,
                    // so the client would deadlock forever waiting for a variable never sent.
                    // Send it from here as soon as this barrier completes, every transition.
                    bool isRemoveItemsBarrier = false;
                    if (message.MessageType == 14 && message.Data is { Length: > 2 } data && data[0] == 1)
                    {
                        using var probe = new MemoryStream(data, writable: false);
                        using var probeReader = new BinaryReader(probe);
                        byte subtype = probeReader.ReadByte();
                        if (subtype == 1)
                        {
                            string name = probeReader.ReadString();
                            if (name == "removeItemsFromWorld")
                                isRemoveItemsBarrier = true;
                        }
                    }
                    multiplayer?.processIncomingMessage(message);
                    if (isRemoveItemsBarrier && Game1.IsMasterGame && Game1.newDaySync?.hasInstance() == true)
                    {
                        Console.WriteLine("[HeadlessNewDay] removeItemsFromWorld barrier done; force-sending farmEvent var to clients.");
                        try
                        {
                            Game1.newDaySync.sendVar<Netcode.NetRef<FarmEvent>, FarmEvent>("farmEvent", Game1.farmEvent);
                            Console.WriteLine(DescribeNewDayState("after-farmEvent-send"));
                        }
                        catch (Exception farmEx)
                        {
                            Console.WriteLine($"[HeadlessNewDay] force farmEvent send failed: {farmEx.Message}");
                        }
                    }
                    if (message.MessageType is 14 or 31)
                        Console.WriteLine(DescribeNewDayState("overnight-after-recv"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Protocol][overnight-recv-error] type={message.MessageType}: {ex}");
                }
                finally
                {
                    message.Dispose();
                }
                processed++;
            }
            if (processed > 0)
                Console.WriteLine($"[Protocol][overnight-pump] processed={processed} thread={Environment.CurrentManagedThreadId}");
        }

        private static string DescribeNewDayState(string checkpoint)
        {
            try
            {
                NewDaySynchronizer? sync = Game1.newDaySync;
                string barrierSummary = "none";
                string variableSummary = "none";
                if (sync != null)
                {
                    FieldInfo? barriersField = typeof(NetSynchronizer).GetField("barriers", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo? variablesField = typeof(NetSynchronizer).GetField("variables", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (barriersField?.GetValue(sync) is Dictionary<string, HashSet<long>> barriers)
                        barrierSummary = string.Join(",", barriers.Select(p => $"{p.Key}:[{string.Join("|", p.Value)}]"));
                    if (variablesField?.GetValue(sync) is IDictionary variables)
                        variableSummary = string.Join(",", variables.Keys.Cast<object>());
                }
                return $"[NewDayCheckpoint] {checkpoint} thread={Environment.CurrentManagedThreadId} day={Game1.dayOfMonth} time={Game1.timeOfDay} newDay={Game1.newDay} active={headlessNewDayActive} syncInstance={sync?.hasInstance()} saved={sync?.hasSaved()} finished={sync?.hasFinished()} " +
                    $"sleep={Game1.netReady?.GetNumberReady("sleep")}/{Game1.netReady?.GetNumberRequired("sleep")} ready_for_save={Game1.netReady?.GetNumberReady("ready_for_save")}/{Game1.netReady?.GetNumberRequired("ready_for_save")} wakeup={Game1.netReady?.GetNumberReady("wakeup")}/{Game1.netReady?.GetNumberRequired("wakeup")} " +
                    $"clients={clientConnections.Count} menu={Game1.activeClickableMenu?.GetType().Name ?? "null"} showingEnd={Game1.showingEndOfNightStuff} hostLoc={Game1.currentLocation?.NameOrUniqueName ?? "null"} barriers={barrierSummary} vars={variableSummary}";
            }
            catch (Exception ex)
            {
                return $"[NewDayCheckpoint] {checkpoint} stateError={ex}";
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting Headless Stardew Valley Server (New Farmhand Customization Stage)...");

            // Load the platform-specific LZ4 native library bundled beside the server.
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string nativeLibraryName = OperatingSystem.IsWindows()
                ? "liblwjgl_lz4.dll"
                : "liblwjgl_lz4.so";
            string nativeDllPath = Path.Combine(baseDir, nativeLibraryName);
            string? gamePath = Environment.GetEnvironmentVariable("VALLEY_GAME_PATH");
            // Also support the common packaged/container layout used by this checkout;
            // Stardew's managed dependencies live beside the game assembly there.
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                string bundledGamePath = @"D:\app\steam\steamapps\common\Stardew Valley";
                if (Directory.Exists(bundledGamePath))
                    gamePath = bundledGamePath;
            }
            if (!File.Exists(nativeDllPath) && !string.IsNullOrWhiteSpace(gamePath))
            {
                nativeDllPath = Path.Combine(gamePath, nativeLibraryName);
            }

            if (File.Exists(nativeDllPath))
            {
                try
                {
                    System.Runtime.InteropServices.NativeLibrary.Load(nativeDllPath);
                    Console.WriteLine($"Successfully loaded native library: {nativeDllPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading native library: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Warning: {nativeLibraryName} not found beside the server.");
            }

            // 1. Set up dependency resolution for referenced assemblies (e.g. Stardew Valley.dll)
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                string? rawName = resolveArgs.Name;
                if (rawName == null) return null;
                string? assemblyName = new AssemblyName(rawName).Name;
                if (assemblyName == null) return null;

                // Look in executable directory first
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assemblyName + ".dll");
                if (File.Exists(path))
                {
                    try
                    {
                        return Assembly.LoadFrom(path);
                    }
                    catch {}
                }

                // Optional game installation path, configured without machine-specific assumptions.
                if (!string.IsNullOrWhiteSpace(gamePath))
                {
                    path = Path.Combine(gamePath!, assemblyName + ".dll");
                    if (File.Exists(path))
                    {
                        try
                        {
                            return Assembly.LoadFrom(path);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error resolving/loading assembly {assemblyName}: {ex.Message}");
                        }
                    }
                }
                return null;
            };

            // Stardew's deterministic hash implementation is used while Farm loads
            // path-layer bushes. Resolve the optional game-install assemblies before
            // map construction (the normal game launcher supplies these implicitly).
            if (!string.IsNullOrEmpty(gamePath))
            {
                AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
                {
                    string? name = new AssemblyName(resolveArgs.Name).Name;
                    if (name == null) return null;
                    string candidate = Path.Combine(gamePath, name + ".dll");
                    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                };
            }

            // 2. Extract actual protocolVersion from Game1 assembly
            try
            {
                var assembly = typeof(Game1).Assembly;
                Console.WriteLine($"Detected Game assembly at: {assembly.Location}");
                var multiplayerType = assembly.GetType("StardewValley.Multiplayer");
                if (multiplayerType != null)
                {
                    var prop = multiplayerType.GetProperty("protocolVersion", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        actualProtocolVersion = prop.GetValue(null) as string;
                        Console.WriteLine($"Detected Stardew Valley Protocol Version: {actualProtocolVersion}");
                    }
                    
                    var protocolVersionOverrideField = multiplayerType.GetField("protocolVersionOverride", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (protocolVersionOverrideField != null && actualProtocolVersion != null)
                    {
                        protocolVersionOverrideField.SetValue(null, actualProtocolVersion);
                        Console.WriteLine($"Overrode Multiplayer.protocolVersionOverride with: {actualProtocolVersion}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading protocolVersion via Reflection: {ex.Message}");
            }

            string targetVersion = actualProtocolVersion ?? "1.6.15";
            Console.WriteLine($"Protocol Version to use: {targetVersion}");

            // 3. Initialize Game1 static state using uninitialized objects and reflection
            Console.WriteLine("Mocking Game1 static fields...");
            
            var serviceContainer = new GameServiceContainer();
            var headlessContent = new HeadlessContentManager(serviceContainer, "Content");
            Game1.content = headlessContent;
            // The content directory is also the authoritative game-install root;
            // resolve managed dependencies shipped beside Stardew Valley.dll.
            string? resolvedGameRoot = Directory.GetParent(HeadlessContentManager.ResolvedContentRoot)?.FullName;
            if (!string.IsNullOrEmpty(resolvedGameRoot))
            {
                AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
                {
                    string? name = new AssemblyName(resolveArgs.Name).Name;
                    if (name == null) return null;
                    string candidate = Path.Combine(resolvedGameRoot, name + ".dll");
                    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                };
            }
            // Farm(string, string) calls updateSeasonalTileSheets during map loading;
            // vanilla normally initializes this in Game1.LoadContent. Install the
            // headless backend before constructing any GameLocation.
            Game1.mapDisplayDevice = new HeadlessDisplayDevice();
            
            // Create uninitialized Game1 instance to bypass constructor & XNA graphics context checks
            var gameInstance = (Game1)FormatterServices.GetUninitializedObject(typeof(Game1));
            Game1.game1 = gameInstance;
            // Options() touches GameRunner.instance through its dirty-state setters. The
            // server only needs networking policy fields, so initialize it without the
            // graphics-bound constructor and set those fields explicitly.
            Game1.options = (Options)FormatterServices.GetUninitializedObject(typeof(Options));
            // Farmer.resetState() clears a mount during the overnight save phase and
            // recalculates running from options.runButton. The graphics-free Options object
            // has no constructor, so initialize the input array that path dereferences.
            Game1.options.runButton = new[] { new InputButton(Keys.LeftShift) };
            Game1.options.ipConnectionsEnabled = true;
            Game1.options.enableFarmhandCreation = true;
            
            var locationsField = typeof(Game1).GetField("_locations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            // The vanilla new-game path creates Farm(mapPath, name), which loads the actual
            // tiled map and its serialized objects before adding default buildings.  The old
            // headless path used Farm() plus reflection, leaving map/terrain/object collections
            // empty (hence no farmhouse, crops, weeds, or cabins for clients to see).
            var locList = new List<GameLocation>();
            if (locationsField != null)
                locationsField.SetValue(gameInstance, locList);

            Game1.whichFarm = 0;
            Game1.uniqueIDForThisGame = (ulong)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
            Game1.season = Season.Spring;
            Game1.dayOfMonth = 1;
            Game1.year = 1;
            // Farmhands need real cabin interiors and beds for spawn and pass-out recovery.
            Game1.startingCabins = 4;
            Game1.cabinsSeparate = false;
            Game1.random = new Random(unchecked((int)Game1.uniqueIDForThisGame));
            // Debris.InitializeChunks draws chunk velocities through this RNG. The vanilla
            // constructor initializes it, but this headless instance bypasses that path.
            Game1.recentMultiplayerRandom = new Random(unchecked((int)Game1.uniqueIDForThisGame));

            var xTileContentField = typeof(Game1).GetField("xTileContent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (xTileContentField != null)
            {
                xTileContentField.SetValue(gameInstance, headlessContent);
            }

            var game1Type = typeof(Game1);
            var multiplayerField = game1Type.GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic);
            Multiplayer? multiplayer = null;
            if (multiplayerField != null)
            {
                multiplayer = new Multiplayer();
                multiplayerField.SetValue(null, multiplayer);
            }

            Game1.netWorldState = new NetRoot<NetWorldState>(new NetWorldState());

            // Mock Program._sdk to NullSDKHelper to prevent Steam API checks
            var programType = typeof(StardewValley.Program);
            var sdkField = programType.GetField("_sdk", BindingFlags.Static | BindingFlags.NonPublic);
            if (sdkField != null)
            {
                var nullSdkHelperType = typeof(StardewValley.SDKs.NullSDKHelper);
                var nullSdkHelper = Activator.CreateInstance(nullSdkHelperType);
                sdkField.SetValue(null, nullSdkHelper);
                Console.WriteLine("Successfully mocked Program._sdk to NullSDKHelper.");
            }

            // Initialize item registry and databases
            try
            {
                var registerMethod = typeof(ItemRegistry).GetMethod("RegisterItemTypes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                registerMethod?.Invoke(null, null);
                Console.WriteLine("ItemRegistry types registered.");

                Game1.objectData = DataLoader.Objects(Game1.content);
                Game1.bigCraftableData = DataLoader.BigCraftables(Game1.content);
                Game1.weaponData = DataLoader.Weapons(Game1.content);
                Game1.toolData = DataLoader.Tools(Game1.content);
                Game1.pantsData = DataLoader.Pants(Game1.content);
                Game1.shirtData = DataLoader.Shirts(Game1.content);
                // Vanilla loads Data/Locations before AddLocations. This catalog carries
                // each location's concrete CLR type, map path, and always-active policy.
                Game1.locationData = DataLoader.Locations(Game1.content);
                Game1.locationContextData = DataLoader.LocationContexts(Game1.content);
                Game1.buildingData = DataLoader.Buildings(Game1.content);
                CraftingRecipe.craftingRecipes = DataLoader.CraftingRecipes(Game1.content);
                CraftingRecipe.cookingRecipes = DataLoader.CookingRecipes(Game1.content);
                Game1.characterData = DataLoader.Characters(Game1.content);
                Game1.cropData = DataLoader.Crops(Game1.content);
                Game1.fruitTreeData = DataLoader.FruitTrees(Game1.content);
                Game1.farmAnimalData = DataLoader.FarmAnimals(Game1.content);
                Game1.petData = DataLoader.Pets(Game1.content);
                Game1.floorPathData = DataLoader.FloorsAndPaths(Game1.content);
                Game1.achievements = DataLoader.Achievements(Game1.content);
                Game1.NPCGiftTastes = DataLoader.NpcGiftTastes(Game1.content);
                Console.WriteLine("Item databases and recipes loaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing ItemRegistry/databases: {ex}");
                Game1.objectData ??= new Dictionary<string, StardewValley.GameData.Objects.ObjectData>();
                Game1.bigCraftableData ??= new Dictionary<string, StardewValley.GameData.BigCraftables.BigCraftableData>();
                Game1.weaponData ??= new Dictionary<string, StardewValley.GameData.Weapons.WeaponData>();
                Game1.toolData ??= new Dictionary<string, StardewValley.GameData.Tools.ToolData>();
                Game1.pantsData ??= new Dictionary<string, StardewValley.GameData.Pants.PantsData>();
                Game1.shirtData ??= new Dictionary<string, StardewValley.GameData.Shirts.ShirtData>();
                Game1.locationContextData ??= new Dictionary<string, LocationContextData>();
                Game1.locationData ??= new Dictionary<string, StardewValley.GameData.Locations.LocationData>();
                Game1.buildingData ??= new Dictionary<string, StardewValley.GameData.Buildings.BuildingData>();
                CraftingRecipe.craftingRecipes ??= new Dictionary<string, string>();
                CraftingRecipe.cookingRecipes ??= new Dictionary<string, string>();
            }

            // Create host player
            var host = new Farmer();
            host.Name = "Host";
            host.farmName.Value = "HeadlessFarm";
            host.UniqueMultiplayerID = 99999999L;
            host.isCustomized.Value = true;
            host.gameVersion = Game1.version ?? targetVersion;
            host.teamRoot = new NetRoot<FarmerTeam>(new FarmerTeam());
            // Vanilla 1.6.15 clients use this synchronized flag to distinguish an
            // automated host (including Message 33 dedicated-host actions). Set the
            // internal NetBool through reflection since ValleyServer links the retail DLL.
            FieldInfo? hasDedicatedHostField = typeof(FarmerTeam).GetField("hasDedicatedHost", BindingFlags.Instance | BindingFlags.NonPublic);
            if (hasDedicatedHostField?.GetValue(host.team) is NetBool hasDedicatedHost)
            {
                hasDedicatedHost.Value = true;
            }
            else
            {
                throw new MissingFieldException(typeof(FarmerTeam).FullName, "hasDedicatedHost");
            }
            foreach (Item tool in Farmer.initialTools())
                host.Items.Add(tool);
            host.Items.IsLocalPlayerInventory = true;
            
            gamePlayerField.SetValue(null, host);
            Game1.serverHost = new NetFarmerRoot(host);
            // Location constructors enumerate online farmers while loading/spawning map
            // objects, so this must exist before vanilla AddLocations (not after it).
            Game1.otherFarmers = new NetRootDictionary<long, Farmer>();
            Game1.otherFarmers.Serializer = SaveSerializer.GetSerializer(typeof(Farmer));

            // Complete vanilla Farm initialization: load the map and objects, then create
            // farmhouse/greenhouse/shipping bin/pet bowl and new-game debris/features.
            var farm = new Farm("Maps\\" + Farm.getMapNameFromTypeInt(Game1.whichFarm), "Farm");
            // Building definitions are loaded above, so create the same default structures as
            // vanilla: farmhouse, greenhouse, shipping bin, pet bowl, and configured cabins.
            // Add the default structures without calling Building.load(); the client loads
            // their interiors/resources after receiving the location. Calling load here
            // requires the complete vanilla location catalog (e.g. FarmHouse).
            farm.AddDefaultBuildings(load: false);
            farm.onNewGame();
            locList.Add(farm);

            // Follow Game1.loadForNewGame: create the canonical location catalog up front.
            // Message 5 must resolve these persistent instances (including Town, Mine,
            // and their specialized subclasses), never synthesize a generic location.
            Game1.AddLocations();
            Game1.flushLocationLookup();
            // Vanilla AddLocations logs and continues when one constructor fails. Ensure
            // Town is still represented by its canonical specialized class, not generic state.
            if (Game1.getLocationFromName("Town") == null)
            {
                var town = new StardewValley.Locations.Town("Maps\\Town", "Town");
                locList.Add(town);
                Game1.flushLocationLookup();
                Console.WriteLine("Recovered canonical Town location using StardewValley.Locations.Town.");
            }
            foreach (GameLocation location in locList)
            {
                multiplayer?.locationRoot(location);
            }
            Console.WriteLine($"Vanilla locations initialized: count={locList.Count}, Town={Game1.getLocationFromName("Town")?.GetType().Name}, Mine={Game1.getLocationFromName("Mine")?.GetType().Name}");

            host.currentLocation = farm;
            Game1.currentLocation = farm;
            if (multiplayer != null)
                multiplayer.locationRoot(farm);
            Console.WriteLine($"Farm initialized: map={farm.mapPath.Value}, buildings={farm.buildings.Count}, objects={farm.objects.Count()}, terrain={farm.terrainFeatures.Count()}, clumps={farm.resourceClumps.Count}");

            // Run the headless instance as the authoritative multiplayer host.  This is
            // important for NetRoot dirty propagation: clients place objects locally
            // using Utility.playerCanPlaceItemHere/tryToPlaceItem, then send the
            // resulting GameLocation delta (message 6) for the host to apply and
            // rebroadcast.  Leaving this at the single-player default (0) silently
            // disables Multiplayer.UpdateLate's location-delta broadcast.
            Game1.multiplayerMode = Game1.multiplayerServer;
            Game1.setGameMode(Game1.playingGameMode);
            Game1.timeOfDay = 600;
            Game1.gameTimeInterval = 0;
            Game1.netWorldState.Value.UpdateFromGame1();
            EnsureFarmhandHomesAndBeds(farm);

            // Load saved farmhands from disk on startup
            LoadSavedFarmhands();
            // Loading the building definitions creates the cabin interior and its starter bed.
            // Do this after the farm is registered, since Cabin's constructor queries Game1.getFarm().
            EnsureFarmhandHomesAndBeds(farm);

            // The location update path queries the music system (getMusicTrackName via
            // isMusicContextActiveButNotPlaying). Its backing dictionary is an instance
            // field on the Game1 singleton that our uninitialized instance never created,
            // so every UpdateWhenCurrentLocation threw NRE partway through 鈥?after debris
            // processing but before terrain features and events. Initialize it so the
            // update completes resource-free; with an empty dictionary the track query
            // resolves to "none" and no audio is ever requested.
            object? game1Instance = typeof(Game1).GetField("game1", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            FieldInfo? requestedTracksField = typeof(Game1).GetField("_instanceRequestedMusicTracks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (game1Instance != null && requestedTracksField != null && requestedTracksField.GetValue(game1Instance) == null)
            {
                requestedTracksField.SetValue(game1Instance, Activator.CreateInstance(requestedTracksField.FieldType));
                Console.WriteLine("Mocked Game1._instanceRequestedMusicTracks for headless music queries.");
            }

            // BuffManager.GetValues marks the HUD buff display dirty for the locally
            // controlled farmer. Debris target selection reads each farmer's magnetic
            // radius through that path, so the host farmer's recalculation must not NRE
            // on the missing UI component. Provide an inert instance (no constructors run,
            // so nothing subscribes to game events).
            if (Game1.buffsDisplay == null)
            {
                Game1.buffsDisplay = (StardewValley.Menus.BuffsDisplay)FormatterServices.GetUninitializedObject(typeof(StardewValley.Menus.BuffsDisplay));
                Console.WriteLine("Mocked Game1.buffsDisplay for headless buff recalculation.");
            }

            // Game1.NewDay() writes nonWarpFade / fadeToBlackAlpha and FadeScreenToBlack
            // through this private ScreenFade instance, which the real Game1 constructor
            // creates. Headless mode skips that constructor, so NewDay() threw a null
            // reference part-way through (after newDay was already set), which then broke
            // the overnight coroutine's barrier pumping. A real ScreenFade with inert
            // callbacks is safe: its state is plain fields and its callbacks are never
            // invoked because headless never runs UpdateFade.
            FieldInfo? screenFadeField = typeof(Game1).GetField("screenFade", BindingFlags.Static | BindingFlags.NonPublic);
            if (screenFadeField != null && screenFadeField.GetValue(null) == null)
            {
                screenFadeField.SetValue(null, new StardewValley.BellsAndWhistles.ScreenFade(() => false, () => { }));
                Console.WriteLine("Mocked Game1.screenFade for headless NewDay/fade writes.");
            }

            // LocalMultiplayer.IsLocalMultiplayer() (called from NetSynchronizer.barrier
            // while the overnight coroutine waits for farmhands) dereferences
            // GameRunner.instance.gameInstances. A headless host never constructs the
            // XNA GameRunner, so give it an inert instance with an empty gameInstances
            // list: IsLocalMultiplayer then safely returns false, which is the desired
            // "remote multiplayer, keep waiting at the barrier" behavior.
            if (GameRunner.instance == null)
            {
                var gameRunner = (GameRunner)FormatterServices.GetUninitializedObject(typeof(GameRunner));
                var gameInstancesField = typeof(GameRunner).GetField("gameInstances", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gameInstancesField != null)
                {
                    gameInstancesField.SetValue(gameRunner, new List<Game1>());
                }
                GameRunner.instance = gameRunner;
                Console.WriteLine("Mocked GameRunner.instance for headless LocalMultiplayer checks.");
            }

            // Multiplayer.allowSyncDelay() calls Game1.newDaySync.hasInstance() on every
            // UpdateEarly/UpdateLate; a null synchronizer made both throw inside the tick's
            // silent catch, so location/world-state/team deltas were NEVER broadcast from
            // the host (debris assignment, farmhandData sync, time-of-day all rely on them).
            // An inert instance reports "no day sync in progress", which is the correct idle
            // state for the headless host.
            if (Game1.newDaySync == null)
            {
                // Use the real constructor: NewDay() later needs a functional synchronizer,
                // not just an object whose idle hasInstance() call happens to work.
                Game1.newDaySync = new NewDaySynchronizer();
                Console.WriteLine("Initialized Game1.newDaySync for headless multiplayer sync.");
            }
            if (Game1.netReady == null)
            {
                Game1.netReady = new ReadySynchronizer();
                Console.WriteLine("Initialized Game1.netReady for multiplayer sleep checks.");
            }
            if (Game1.dedicatedServer == null)
            {
                Game1.dedicatedServer = new DedicatedServer();
                Console.WriteLine("Initialized Game1.dedicatedServer for dedicated-host sleep handling.");
            }
            // NetSynchronizer.barrier() calls Game1.hooks.AfterNewDayBarrier after each
            // overnight barrier completes. A real Game1 constructor creates hooks; the
            // headless mock must provide one or the overnight coroutine throws NullReference.
            var hooksField = typeof(Game1).GetField("hooks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (hooksField != null && hooksField.GetValue(null) == null)
            {
                hooksField.SetValue(null, new StardewValley.Mods.ModHooks());
                Console.WriteLine("Initialized Game1.hooks for overnight AfterNewDayBarrier callbacks.");
            }
            // Game1's constructor normally creates this static collection. It is used by
            // ready checks and team updates, so install the same empty collection here.
            FieldInfo? onlineFarmersField = typeof(Game1).GetField("_onlineFarmers", BindingFlags.Static | BindingFlags.NonPublic);
            if (onlineFarmersField != null && onlineFarmersField.GetValue(null) == null)
            {
                onlineFarmersField.SetValue(null, new FarmerCollection());
                Console.WriteLine("Initialized Game1._onlineFarmers for headless ready checks.");
            }

            Console.WriteLine("Game1 static fields mocked successfully!");

            // 4. Initialize Lidgren NetServer
            NetPeerConfiguration config = new NetPeerConfiguration("StardewValley");
            config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            config.Port = GetPort(args);
            Console.WriteLine($"Using listen port {config.Port} (client config must use the same port).");
            config.ConnectionTimeout = 30f;
            config.PingInterval = 5f;
            config.MaximumConnections = 8 * 2;
            config.MaximumTransmissionUnit = 1200;

            NetServer server = new NetServer(config);
            server.Start();
            Console.WriteLine($"Server started and listening on port {config.Port}...");
            Game1.server = new HeadlessGameServer(server, clientConnections);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long lastTickTime = stopwatch.ElapsedMilliseconds;
            int lastLoggedGameTime = Game1.timeOfDay;
            const long msPerTick = 16; // ~60 ticks per second
            Console.WriteLine("[HeadlessClock] Paused: no active farmhand connections.");

            // Validate the server-side half of the vanilla pickup chain before accepting
            // clients: target assignment must work for an inhabited location, or a real
            // client could never receive Debris.player and pick anything up.
            RunDebrisSelfTest(farm);

            // 5. Message loop. Ctrl+C/console shutdown stops accepting work cleanly.
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (!shutdown.IsCancellationRequested)
                {
                    shutdown.Cancel();
                    Console.WriteLine("Shutdown requested; finishing queued messages...");
                }
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (!shutdown.IsCancellationRequested)
                    shutdown.Cancel();
            };

            while (!shutdown.IsCancellationRequested)
            {
                NetIncomingMessage inc;
                while (!shutdown.IsCancellationRequested && (inc = server.ReadMessage()) != null)
                {
                    try
                    {
                    switch (inc.MessageType)
                    {
                        case NetIncomingMessageType.DiscoveryRequest:
                            Console.WriteLine($"Received DiscoveryRequest from {inc.SenderEndPoint}. Replying with protocol version {targetVersion}...");
                            NetOutgoingMessage response = server.CreateMessage();
                            response.Write(targetVersion);
                            response.Write("Headless Stardew Valley Server");
                            server.SendDiscoveryResponse(response, inc.SenderEndPoint);
                            break;

                        case NetIncomingMessageType.ConnectionApproval:
                            Console.WriteLine($"Received ConnectionApproval from {inc.SenderEndPoint}. Approving...");
                            inc.SenderConnection.Approve();
                            break;

                        case NetIncomingMessageType.StatusChanged:
                            var status = (NetConnectionStatus)inc.ReadByte();
                            string reason = inc.ReadString();
                            Console.WriteLine($"Status changed for {inc.SenderEndPoint}: {status} (Reason: {reason})");

                            if (status == NetConnectionStatus.Connected)
                            {
                                Console.WriteLine($"Client {inc.SenderEndPoint} connected successfully. Preparing and sending available farmhands...");
                                Console.WriteLine($"Current clientConnections keys: {string.Join(", ", clientConnections.Keys)}");
                                Console.WriteLine($"Current Game1.otherFarmers.Roots keys: {string.Join(", ", Game1.otherFarmers.Roots.Keys)}");
                                
                                var availableList = new List<Farmer>();
                                
                                // Add all saved farmhands (from the selection catalog) that are customized
                                // and not currently connected/active. The catalog is the authoritative list
                                // of every character ever created; otherFarmers only holds online players.
                                foreach (var kvp in savedFarmhandCatalog)
                                {
                                    var fh = kvp.Value;
                                    if (fh != null && fh.isCustomized.Value)
                                    {
                                        Console.WriteLine($"Checking saved farmhand: Name={fh.Name}, ID={fh.UniqueMultiplayerID}, isCustomized={fh.isCustomized.Value}");
                                        // A farmhand counts as "active" only when its mapped transport connection is
                                        // actually connected. A leftover mapping from a client that exited without a
                                        // clean disconnect would otherwise hide the farmhand from the selectable list.
                                        bool liveActive = clientConnections.TryGetValue(fh.UniqueMultiplayerID, out var conn)
                                                         && conn.Status == NetConnectionStatus.Connected;
                                        if (!liveActive)
                                        {
                                            availableList.Add(fh);
                                            Console.WriteLine($"Adding saved farmhand to available list: {fh.Name} ({fh.UniqueMultiplayerID})");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Skipping active farmhand: {fh.Name} ({fh.UniqueMultiplayerID})");
                                        }
                                    }
                                }

                                // If we have less than 4 farmhands, add a "New Farmhand" slot!
                                if (availableList.Count < 4)
                                {
                                    long newId = 11111111L;
                                    while (savedFarmhandCatalog.ContainsKey(newId) || availableList.Any(f => f.UniqueMultiplayerID == newId))
                                    {
                                        newId += 1;
                                    }
                                    
                                    // Vanilla Game1.resetPlayer/Cabin uses Farmer.initialTools(); preserve canonical ordering.
                                     var farmhand = new Farmer(new FarmerSprite(null), Vector2.Zero, 1, "", Farmer.initialTools(), isMale: true);
                                    farmhand.UniqueMultiplayerID = newId;
                                    farmhand.isCustomized.Value = false;
                                    farmhand.gameVersion = Game1.version ?? targetVersion;
                                    // Give every newly-created farmhand the standard starter parsnip seeds.
                                    farmhand.Items.Add(ItemRegistry.Create("(O)472", 15));
                                    
                                    availableList.Add(farmhand);
                                    Console.WriteLine($"Added new farmhand slot with ID: {newId}");
                                }

                                byte[] payloadBytes;
                                using (var memStream = new MemoryStream())
                                {
                                    using (var writer = new BinaryWriter(memStream))
                                    {
                                        // FarmhandMenu expects the host's current world date.
                                        writer.Write(Game1.year);
                                        writer.Write(Game1.seasonIndex);
                                        writer.Write(Game1.dayOfMonth);
                                        writer.Write((byte)availableList.Count); // available farmhands count
                                        
                                        foreach (var fh in availableList)
                                        {
                                            var farmhandRoot = new NetFarmerRoot(fh)
                                            {
                                                Serializer = SaveSerializer.GetSerializer(typeof(Farmer))
                                            };
                                            farmhandRoot.WriteFull(writer);
                                            farmhandRoot.Serializer = null;
                                        }
                                        
                                        payloadBytes = memStream.ToArray();
                                    }
                                }

                                var outgoingMsg = new OutgoingMessage(9, Game1.player.UniqueMultiplayerID, new object[] { payloadBytes });
                                var netOutgoingMsg = server.CreateMessage();
                                MockLidgrenMessageUtils.WriteMessage(outgoingMsg, netOutgoingMsg);
                                server.SendMessage(netOutgoingMsg, inc.SenderConnection, NetDeliveryMethod.ReliableOrdered);
                                Console.WriteLine("Sent available farmhands (Message 9) to client!");
                            }
                            else if (status == NetConnectionStatus.Disconnected || status == NetConnectionStatus.None)
                            {
                                Console.WriteLine($"Client {inc.SenderEndPoint} disconnected. Saving all active farmhands...");
                                SaveAllActiveFarmhands();

                                // Clean up connection mapping
                                long idToRemove = -1;
                                foreach (var kvp in clientConnections)
                                {
                                    if (kvp.Value == inc.SenderConnection)
                                    {
                                        idToRemove = kvp.Key;
                                        break;
                                    }
                                }
                                if (idToRemove != -1)
                                {
                                    clientConnections.Remove(idToRemove);
                                    Console.WriteLine($"Removed mapping for player {idToRemove}");
                                    if (clientConnections.Count == 0)
                                    {
                                        Console.WriteLine($"[HeadlessClock] Paused: last active farmhand {idToRemove} disconnected.");
                                    }

                                    // Clean up the disconnected farmhand from the online set only. It
                                    // stays in savedFarmhandCatalog (and is already persisted by
                                    // SaveAllActiveFarmhands above), so it remains selectable the next
                                    // time the player connects.
                                    Game1.otherFarmers.TryGetValue(idToRemove, out var disconnectedFarmer);
                                    Game1.otherFarmers.Roots.Remove(idToRemove);
                                    Console.WriteLine($"Removed online farmhand {idToRemove}; kept in selection catalog.");

                                    // Notify other clients about the disconnection
                                    if (disconnectedFarmer != null)
                                    {
                                        var discMsg = new OutgoingMessage(19, disconnectedFarmer);
                                        foreach (var connKvp in clientConnections)
                                        {
                                            if (connKvp.Key != idToRemove)
                                            {
                                                var msg = server.CreateMessage();
                                                MockLidgrenMessageUtils.WriteMessage(discMsg, msg);
                                                server.SendMessage(msg, connKvp.Value, NetDeliveryMethod.ReliableOrdered);
                                            }
                                        }
                                        Console.WriteLine($"Broadcasted player {idToRemove} disconnect (Message 19) to remaining clients.");
                                    }
                                }
                            }
                            break;

                        case NetIncomingMessageType.Data:
                            IncomingMessage incomingMsg = new IncomingMessage();
                            using (NetBufferReadStream stream = new NetBufferReadStream(inc))
                            {
                                while (inc.LengthBits - inc.Position >= 8)
                                {
                                    MockLidgrenMessageUtils.ReadStreamToMessage(stream, incomingMsg);
                                     if (incomingMsg.MessageType is 14 or 30 or 31)
                                     {
                                         Console.WriteLine($"[Protocol][recv] thread={Environment.CurrentManagedThreadId} farmer={incomingMsg.FarmerID} {ProtocolMessages.DescribeIncomingMessage(incomingMsg)}");
                                         Console.WriteLine(DescribeNewDayState("before-recv"));
                                     }
                                     receivedMessageTypeCounts.TryGetValue(incomingMsg.MessageType, out long msgTypeCount);
                                     receivedMessageTypeCounts[incomingMsg.MessageType] = msgTypeCount + 1;
                                     if (incomingMsg.MessageType == 2)
                                     {
                                         Console.WriteLine("Received PlayerIntroduction (Message 2) from client!");
                                         
                                         var clientFarmerRoot = new NetFarmerRoot
                                          {
                                              Serializer = SaveSerializer.GetSerializer(typeof(Farmer))
                                          };
                                         clientFarmerRoot.ReadConnectionPacket(incomingMsg.Reader);
                                         var clientFarmer = clientFarmerRoot.Value;
                                         long newClientId = clientFarmer.UniqueMultiplayerID;
                                         Console.WriteLine($"Client requested farmhand ID: {newClientId}, Name: {clientFarmer.Name}");

                                         // Register client farmhand
                                         Game1.otherFarmers.Roots[newClientId] = clientFarmerRoot;

                                         // Keep the selection catalog authoritative for this id as well.
                                         savedFarmhandCatalog[newClientId] = clientFarmer;

                                         // Also register in the world-state farmhand directory. In 1.6.15
                                         // NetFarmerRef resolves its target through Game1.getAllFarmers(),
                                         // which enumerates netWorldState.farmhandData (not otherFarmers).
                                         // Debris target assignment writes the ref but every read back
                                         // (host and clients alike) would return null without this entry,
                                         // so drops could never be assigned or picked up. The entry is
                                         // synced to all clients through the netWorldState delta.
                                         Game1.netWorldState.Value.farmhandData[newClientId] = clientFarmer;
                                         Console.WriteLine($"Registered farmhand {newClientId} in world state farmhandData (Message 2).");

                                         // Ensure the farmhand is bound to a cabin before any spawn/pass-out handling.
                                         // This must happen after registration so NetFarmerRef can resolve it.
                                         EnsureFarmhandHomesAndBeds(Game1.getFarm());


                                         // A transport-level Connected status isn't an active player yet. Only
                                         // begin advancing time after the farmhand introduction handshake has
                                         // completed and the connection is mapped to that farmhand.
                                         bool wasClockPaused = clientConnections.Count == 0;
                                         clientConnections[newClientId] = inc.SenderConnection;
                                         Console.WriteLine($"Mapped connection for player {newClientId}");
                                         if (wasClockPaused)
                                         {
                                             // The handshake can be processed after a long/expensive message-loop
                                             // pass. Discard that pass's elapsed time so none of the preceding
                                             // playerless interval leaks into the clock on resume.
                                             discardNextHeadlessClockElapsed = true;
                                             Console.WriteLine($"[HeadlessClock] Resumed: first active farmhand {newClientId} completed handshake.");
                                         }

                                         // Send ServerIntroduction (Message 1)
                                         Console.WriteLine("Sending ServerIntroduction (Message 1)...");
                                         byte[] hostBytes = WriteObjectFullBytes(Game1.serverHost, newClientId);
                                         byte[] teamBytes = WriteObjectFullBytes(Game1.player.teamRoot, newClientId);
                                         byte[] worldStateBytes = WriteObjectFullBytes(Game1.netWorldState, newClientId);

                                         var introMsg = new OutgoingMessage(1, Game1.player.UniqueMultiplayerID, new object[] { hostBytes, teamBytes, worldStateBytes });
                                         var netIntroMsg = server.CreateMessage();
                                         MockLidgrenMessageUtils.WriteMessage(introMsg, netIntroMsg);
                                         server.SendMessage(netIntroMsg, inc.SenderConnection, NetDeliveryMethod.ReliableOrdered);
                                         Console.WriteLine("Sent ServerIntroduction!");

                                         // Send LocationIntroduction (Message 3) for "Farm" location with force_current = true
                                         Console.WriteLine("Sending LocationIntroduction (Message 3)...");
                                         var location = Game1.getLocationFromName("Farm");
                                         // CRITICAL: send the location's canonical multiplayer root (the one the
                                         // tick's UpdateLate -> broadcastLocationDeltas serializes), never a fresh
                                         // NetRoot wrapping the same GameLocation. A throwaway root desynchronizes
                                         // the client: server-side deltas (weed removal, debris target assignment,
                                         // placements) target the canonical root while the client holds the
                                         // temporary one, so world changes never reach it and its own edits are
                                         // never reconciled with the host's authoritative copy.
                                         NetRoot<GameLocation> locRoot = multiplayer?.locationRoot(location)
                                             ?? throw new InvalidOperationException($"Location '{location.NameOrUniqueName}' has no multiplayer NetRoot.");
                                         byte[] locationBytes = WriteObjectFullBytes(locRoot, newClientId);

                                         var locMsg = new OutgoingMessage(3, Game1.player.UniqueMultiplayerID, new object[] { true, locationBytes });
                                         var netLocMsg = server.CreateMessage();
                                         MockLidgrenMessageUtils.WriteMessage(locMsg, netLocMsg);
                                         server.SendMessage(netLocMsg, inc.SenderConnection, NetDeliveryMethod.ReliableOrdered);
                                         Console.WriteLine("Sent LocationIntroduction!");

                                         // Introduce new client to existing clients, and vice versa
                                         foreach (var rootKvp in Game1.otherFarmers.Roots)
                                         {
                                             long otherId = rootKvp.Key;
                                             if (otherId != newClientId && otherId != 99999999L && otherId != 0)
                                             {
                                                 if (clientConnections.TryGetValue(otherId, out var otherConn))
                                                 {
                                                     // 1. Send new client's introduction to existing client (otherId)
                                                     Console.WriteLine($"Introducing new player {newClientId} to existing player {otherId}...");
                                                     byte[] newClientBytes = WriteObjectFullBytes(clientFarmerRoot, otherId);
                                                     var introToExisting = new OutgoingMessage(2, clientFarmer, new object[] { "Player", newClientBytes });
                                                     var netMsgToExisting = server.CreateMessage();
                                                     MockLidgrenMessageUtils.WriteMessage(introToExisting, netMsgToExisting);
                                                     server.SendMessage(netMsgToExisting, otherConn, NetDeliveryMethod.ReliableOrdered);

                                                     // 2. Send existing client's introduction to new client (newClientId)
                                                     Console.WriteLine($"Introducing existing player {otherId} to new player {newClientId}...");
                                                     byte[] existingClientBytes = WriteObjectFullBytes(rootKvp.Value, newClientId);
                                                     var introToNew = new OutgoingMessage(2, rootKvp.Value.Value, new object[] { "Player", existingClientBytes });
                                                     var netMsgToNew = server.CreateMessage();
                                                     MockLidgrenMessageUtils.WriteMessage(introToNew, netMsgToNew);
                                                     server.SendMessage(netMsgToNew, inc.SenderConnection, NetDeliveryMethod.ReliableOrdered);
                                                 }
                                             }
                                         }
                                     }
                                    else if (incomingMsg.MessageType == 5)
                                    {
                                        try
                                        {
                                            short x = incomingMsg.Reader.ReadInt16();
                                            short y = incomingMsg.Reader.ReadInt16();
                                            string name = incomingMsg.Reader.ReadString();
                                            byte flags = incomingMsg.Reader.ReadByte();
                                            bool isStructure = (flags & 1) != 0;
                                            bool warpingForForcedRemoteEvent = (flags & 2) != 0;
                                            bool needsLocationInfo = (flags & 4) != 0;
                                            int facingDirection = (flags & 0x10) != 0 ? 1
                                                : (flags & 0x20) != 0 ? 2
                                                : (flags & 0x40) != 0 ? 3
                                                : 0;

                                            var farmer = incomingMsg.SourceFarmer;
                                            if (farmer != null && needsLocationInfo)
                                            {
                                                // Match GameServer.warpFarmer: require a canonical location,
                                                // update the authoritative farmer, and send its persistent root.
                                                // Never add a generic fallback which can poison location lookup.
                                                GameLocation location = Game1.RequireLocation(name, isStructure);
                                                if (Game1.IsMasterGame)
                                                {
                                                    location.hostSetup();
                                                }
                                                farmer.currentLocation = location;
                                                farmer.Position = new Vector2(x * 64, y * 64 - (farmer.Sprite.getHeight() - 32) + 16);

                                                NetRoot<GameLocation> locationRoot = multiplayer?.locationRoot(location)
                                                    ?? throw new InvalidOperationException($"Location '{location.NameOrUniqueName}' has no multiplayer NetRoot.");
                                                byte[] locationBytes = WriteObjectFullBytes(locationRoot, farmer.UniqueMultiplayerID);

                                                var locMsg = new OutgoingMessage(3, Game1.player.UniqueMultiplayerID, new object[] { false, locationBytes });
                                                var netLocMsg = server.CreateMessage();
                                                MockLidgrenMessageUtils.WriteMessage(locMsg, netLocMsg);
                                                server.SendMessage(netLocMsg, inc.SenderConnection, NetDeliveryMethod.ReliableOrdered);
                                                Console.WriteLine($"Warp {farmer.UniqueMultiplayerID}: {location.NameOrUniqueName} ({location.GetType().Name}) at {x},{y}, facing={facingDirection}, forcedEvent={warpingForForcedRemoteEvent}; sent canonical location root.");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error processing warp: {ex}");
                                        }
                                    }
                                    else
                                    {
                                        try
                                        {
                                            // Type-31 = NetReady sync (ReadyCheck/Lock/Finish). What a sleeping
                                            // farmhand uses to reach agreement with the host. Detect whether a
                                            // "sleep" check actually exists on the server after each such message.
                                            bool isReadyMsg = incomingMsg.MessageType == 31;
                                            var mp = typeof(Game1).GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) as Multiplayer;
                                            bool deferToOvernightWorker = headlessNewDayActive && incomingMsg.MessageType is 14 or 31;
                                            if (deferToOvernightWorker)
                                            {
                                                deferredOvernightMessages.Enqueue(ProtocolMessages.Clone(incomingMsg));
                                                Console.WriteLine($"[Protocol][deferred] type={incomingMsg.MessageType} queued={deferredOvernightMessages.Count}");
                                            }
                                            else if (incomingMsg.MessageType != 19)
                                            {
                                                mp?.processIncomingMessage(incomingMsg);
                                                if (incomingMsg.MessageType is 14 or 31)
                                                    Console.WriteLine(DescribeNewDayState("after-recv"));
                                            }
                                            if (isReadyMsg && !deferToOvernightWorker && Game1.netReady != null)
                                            {
                                                Game1.netReady.Update();
                                                Console.WriteLine($"[Ready31] farmer={incomingMsg.FarmerID} after ready(sleep)={Game1.netReady.GetNumberReady("sleep")} req(sleep)={Game1.netReady.GetNumberRequired("sleep")} isReady={Game1.netReady.IsReady("sleep")}");
                                            }
                                            
                                            // Check if the source farmer has completed customization and needs saving
                                            var farmer = incomingMsg.SourceFarmer;
                                             if (farmer != null && farmer.UniqueMultiplayerID != 99999999L && farmer.UniqueMultiplayerID != 0 && farmer.isCustomized.Value && !savedFarmerIds.Contains(farmer.UniqueMultiplayerID))
                                            {
                                                // Ensure every newly-created farmhand carries the standard starter parsnip seeds.
                                                if (!farmer.Items.Any(i => i != null && i.QualifiedItemId == "(O)472"))
                                                {
                                                    farmer.Items.Add(ItemRegistry.Create("(O)472", 15));
                                                    Console.WriteLine($"Added starter parsnip seeds to farmhand {farmer.Name} ({farmer.UniqueMultiplayerID}).");
                                                }
                                                Console.WriteLine($"Farmer {farmer.Name} ({farmer.UniqueMultiplayerID}) completed customization. Saving...");
                                                SaveFarmhand(farmer);
                                                savedFarmerIds.Add(farmer.UniqueMultiplayerID);
                                            }

                                            // Rebroadcast client broadcast messages to other clients
                                             if (mp?.isClientBroadcastType(incomingMsg.MessageType) ?? false)
                                            {
                                                var outMsg = new OutgoingMessage(incomingMsg);
                                                BroadcastMessage(outMsg, server, inc.SenderConnection);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error processing message {incomingMsg.MessageType}: {ex}");
                                        }
                                    }
                                }
                            }
                            break;

                        default:
                            break;
                    }
                    }
                    catch (Exception ex)
                    {
                        // Isolate malformed messages and per-client handler failures.
                        Console.WriteLine($"Error handling {inc.MessageType} from {inc.SenderEndPoint}: {ex}");
                    }
                    finally
                    {
                        server.Recycle(inc);
                    }
                }

                long currentTime = stopwatch.ElapsedMilliseconds;
                if (currentTime - lastTickTime >= msPerTick)
                {
                    long elapsedMilliseconds = currentTime - lastTickTime;
                    lastTickTime = currentTime;
                    Game1.ticks++;

                    // Don't call Game1.UpdateGameClock here. Its ten-minute handler runs
                    // presentation/content code (music and LocationData lookups) which a
                    // resource-free dedicated server intentionally doesn't initialize.
                    // clientConnections is populated only by a completed PlayerIntroduction
                    // farmhand handshake, so raw Lidgren connections don't unpause the clock.
                    // lastTickTime is refreshed on every tick even while paused, preventing
                    // offline wall-clock time from being charged when the first player joins.
                    if (clientConnections.Count > 0 && !headlessNewDayActive)
                    {
                        if (discardNextHeadlessClockElapsed)
                        {
                            discardNextHeadlessClockElapsed = false;
                        }
                        else
                        {
                            AdvanceHeadlessClock(elapsedMilliseconds);
                        }
                    }
                    if (Game1.timeOfDay != lastLoggedGameTime)
                    {
                        Console.WriteLine($"Game time advanced: {lastLoggedGameTime} -> {Game1.timeOfDay}");
                        lastLoggedGameTime = Game1.timeOfDay;
                    }

                    if (Game1.netWorldState != null)
                    {
                        Game1.netWorldState.Value.UpdateFromGame1();
                    }

                    // Vanilla host flow (Game1.UpdateLocations -> _UpdateLocation): inhabited
                    // locations run UpdateWhenCurrentLocation, whose debris.RemoveWhere(updateChunks)
                    // assigns each debris a target via findBestPlayer because the host is the master
                    // game. The actual chunk movement and collection intentionally run on the owning
                    // farmhand's CLIENT inside updateChunks' IsLocalPlayer branch; the item then
                    // reaches the server through that client's farmer root delta (message 0), which
                    // processIncomingMessage applies and isClientBroadcastType relays. The host must
                    // not collect on behalf of clients: broadcastFarmerDeltas only ever syncs the
                    // Game1.player root, so items collected into a remote farmhand's inventory here
                    // would be deleted from the location delta without ever reaching that client.
                    headlessSimulationTimeMs += elapsedMilliseconds;
                    var simulationTime = new GameTime(
                        TimeSpan.FromMilliseconds(headlessSimulationTimeMs),
                        TimeSpan.FromMilliseconds(elapsedMilliseconds));
                    Game1.currentGameTime = simulationTime;
                    if (!headlessNewDayActive)
                    {
                        UpdateHeadlessLocations(simulationTime);
                    }

                    try
                    {
                     var mp = typeof(Game1).GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) as Multiplayer;
                     if (mp != null)
                     {
                         if (headlessNewDayActive)
                         {
                             if (currentTime - lastNewDayMonitorMs >= 1000)
                             {
                                 lastNewDayMonitorMs = currentTime;
                                 Console.WriteLine(DescribeNewDayState("monitor"));
                             }
                         }
                         else
                         {
                             mp.UpdateEarly();
                             Game1.dedicatedServer?.Tick();
                             Game1.netReady?.Update();
                             EnsureHeadlessDedicatedHostFlag();
                             PrepareHeadlessHostForSleep();
                             PumpHeadlessNewDayProcess();
                             mp.UpdateLate();
                         }
                     }
                    }
                    catch (Exception ex)
                    {
                        // These calls carry every host->client sync (location deltas with debris
                        // assignment, world-state farmhandData, team/player deltas). A silent
                        // failure here made pickups impossible to diagnose, so surface the first one.
                        if (!updateLateErrorLogged)
                        {
                            updateLateErrorLogged = true;
                            Console.WriteLine($"[MultiplayerSync] First UpdateEarly/UpdateLate failure: {ex}");
                        }
                    }

                    // Forward queued messages
                    foreach (var farmer in Game1.otherFarmers.Values)
                    {
                        if (farmer.messageQueue.Count > 0 && clientConnections.TryGetValue(farmer.UniqueMultiplayerID, out var conn))
                        {
                            foreach (var outMsg in farmer.messageQueue)
                            {
                                var msg = server.CreateMessage();
                                MockLidgrenMessageUtils.WriteMessage(outMsg, msg);
                                server.SendMessage(msg, conn, NetDeliveryMethod.ReliableOrdered);
                            }
                            farmer.messageQueue.Clear();
                        }
                    }

                    if (Game1.player != null && Game1.player.messageQueue.Count > 0)
                    {
                        foreach (var outMsg in Game1.player.messageQueue)
                        {
                            foreach (var connKvp in clientConnections)
                            {
                                var msg = server.CreateMessage();
                                MockLidgrenMessageUtils.WriteMessage(outMsg, msg);
                                server.SendMessage(msg, connKvp.Value, NetDeliveryMethod.ReliableOrdered);
                            }
                        }
                        Game1.player.messageQueue.Clear();
                    }
                }
                Thread.Sleep(1);
            }
        }

    }
}
