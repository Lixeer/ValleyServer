#pragma warning disable SYSLIB0050

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Lidgren.Network;
using Netcode;
using StardewValley;
using StardewValley.Network;
using StardewValley.Buffs;
using StardewValley.SaveSerialization;
using StardewValley.GameData.LocationContexts;
using ValleyServer.Core;

namespace ValleyServer.Adapters
{
    public class HeadlessContentManager : LocalizedContentManager
    {
        private readonly string _cachedContentRoot;

        public HeadlessContentManager(IServiceProvider serviceProvider, string rootDirectory, string steamDirPath)
            : base(serviceProvider, rootDirectory)
        {
            _cachedContentRoot = Path.Combine(steamDirPath, "Content");
        }

        public override LocalizedContentManager CreateTemporary()
        {
            return new HeadlessContentManager(base.ServiceProvider, base.RootDirectory, Path.GetDirectoryName(_cachedContentRoot)!);
        }

        protected override Stream OpenStream(string assetName)
        {
            string suffix = assetName;
            if (suffix.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(8);
            }
            else if (suffix.StartsWith("Content\\", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(8);
            }

            string path = Path.Combine(_cachedContentRoot, suffix);
            if (!File.Exists(path))
            {
                if (File.Exists(path + ".xnb"))
                {
                    path += ".xnb";
                }
            }

            return File.OpenRead(path);
        }

        public override T Load<T>(string assetName)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.Texture2D));
            }
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.SpriteFont));
            }
            return base.Load<T>(assetName);
        }

        public override T Load<T>(string assetName, LanguageCode language)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.Texture2D));
            }
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.SpriteFont));
            }
            return base.Load<T>(assetName, language);
        }

        public override T LoadImpl<T>(string baseAssetName, string localizedAssetName, LanguageCode languageCode)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.Texture2D));
            }
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return (T)(object)FormatterServices.GetUninitializedObject(typeof(Microsoft.Xna.Framework.Graphics.SpriteFont));
            }
            return base.LoadImpl<T>(baseAssetName, localizedAssetName, languageCode);
        }

        public override bool DoesAssetExist<T>(string assetName)
        {
            if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D) ||
                typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont))
            {
                return true;
            }

            try
            {
                if (base.DoesAssetExist<T>(assetName))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }

            string suffix = assetName;
            if (suffix.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(8);
            }
            else if (suffix.StartsWith("Content\\", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(8);
            }

            string path = Path.Combine(_cachedContentRoot, suffix);
            if (File.Exists(path) || File.Exists(path + ".xnb"))
            {
                return true;
            }

            return false;
        }
    }

    public static class MockLidgrenMessageUtils
    {
        private static MethodInfo? writeMessageMethod = null;
        private static MethodInfo? readStreamToMessageMethod = null;

        static MockLidgrenMessageUtils()
        {
            var type = typeof(LidgrenMessageUtils);
            writeMessageMethod = type.GetMethod("WriteMessage", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            readStreamToMessageMethod = type.GetMethod("ReadStreamToMessage", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        public static void WriteMessage(OutgoingMessage srcMsg, NetOutgoingMessage destMsg)
        {
            writeMessageMethod?.Invoke(null, new object[] { srcMsg, destMsg });
        }

        public static void ReadStreamToMessage(NetBufferReadStream stream, IncomingMessage msg)
        {
            readStreamToMessageMethod?.Invoke(null, new object[] { stream, msg });
        }
    }

    public class HeadlessGameServer : IGameServer
    {
        private readonly NetServer _netServer;
        private readonly ISessionManager _sessionManager;

        public HeadlessGameServer(NetServer netServer, ISessionManager sessionManager)
        {
            _netServer = netServer;
            _sessionManager = sessionManager;
        }

        public int connectionsCount => _netServer.Connections.Count;
        public BandwidthLogger? BandwidthLogger => null;
        public bool LogBandwidth { get => false; set {} }

        public string getInviteCode() => "";
        public string getUserName(long farmerId) => "Player";
        public void setPrivacy(ServerPrivacy privacy) {}
        public void stopServer() {}
        public void receiveMessages() {}
        public bool canAcceptIPConnections() => true;
        public bool canOfferInvite() => false;
        public void offerInvite() {}
        public bool connected() => true;
        public void sendMessages() {}
        public void startServer() {}
        public void initializeHost() {}
        public void sendServerIntroduction(long peer) {}
        public void kick(long disconnectee) {}
        public string ban(long farmerId) => "";
        public void playerDisconnected(long disconnectee) {}
        public bool isGameAvailable() => true;
        public bool whenGameAvailable(Action action, Func<bool> customAvailabilityCheck = null!) { action(); return true; }
        public void checkFarmhandRequest(string userId, string connectionId, NetFarmerRoot farmer, Action<OutgoingMessage> sendMessage, Action approve) {}
        public void sendAvailableFarmhands(string userId, string connectionId, Action<OutgoingMessage> sendMessage) {}
        public void processIncomingMessage(IncomingMessage message) {}
        public void updateLobbyData() {}
        public float getPingToClient(long peer) => 0f;
        public bool isUserBanned(string userID) => false;
        public void onConnect(string connectionID) {}
        public void onDisconnect(string connectionID) {}
        public bool IsLocalMultiplayerInitiatedServer() => false;

        public void sendMessage(long peerId, OutgoingMessage message)
        {
            var conn = _sessionManager.GetConnection(peerId);
            if (conn != null)
            {
                var msg = _netServer.CreateMessage();
                MockLidgrenMessageUtils.WriteMessage(message, msg);
                _netServer.SendMessage(msg, conn, NetDeliveryMethod.ReliableOrdered);
            }
        }

        public void sendMessage(long peerId, byte messageType, Farmer sourceFarmer, params object[] data)
        {
            this.sendMessage(peerId, new OutgoingMessage(messageType, sourceFarmer, data));
        }
    }

    public class GameEngineAdapter : IGameEngineAdapter
    {
        private readonly ISessionManager _sessionManager;
        private readonly string _savedFarmhandsPath = @".\farms";
        private readonly HashSet<long> _savedFarmerIds = new HashSet<long>();
        
        public string TargetVersion { get; private set; } = "1.6.15";

        public GameEngineAdapter(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        public void Initialize(string steamDirPath, string gameDllPath)
        {
            // 1. Load native liblwjgl_lz4.dll
            string nativeDllPath = Path.Combine(steamDirPath, "liblwjgl_lz4.dll");
            if (File.Exists(nativeDllPath))
            {
                try
                {
                    System.Runtime.InteropServices.NativeLibrary.Load(nativeDllPath);
                    Console.WriteLine($"[Adapter] Loaded native DLL: {nativeDllPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Adapter] Error loading native DLL: {ex.Message}");
                }
            }

            // 2. Set up assembly resolve handler
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                string? rawName = resolveArgs.Name;
                if (rawName == null) return null;
                string? assemblyName = new AssemblyName(rawName).Name;
                if (assemblyName == null) return null;
                string path = Path.Combine(steamDirPath, assemblyName + ".dll");
                if (File.Exists(path))
                {
                    try
                    {
                        return Assembly.LoadFrom(path);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Adapter] Error resolving/loading assembly {assemblyName}: {ex.Message}");
                    }
                }
                return null;
            };

            // 3. Load game DLL and extract actual protocolVersion
            if (File.Exists(gameDllPath))
            {
                try
                {
                    Console.WriteLine($"[Adapter] Loading game assembly: {gameDllPath}");
                    var assembly = Assembly.LoadFrom(gameDllPath);
                    var multiplayerType = assembly.GetType("StardewValley.Multiplayer");
                    if (multiplayerType != null)
                    {
                        var prop = multiplayerType.GetProperty("protocolVersion", BindingFlags.Public | BindingFlags.Static);
                        if (prop != null)
                        {
                            var version = prop.GetValue(null) as string;
                            if (version != null)
                            {
                                TargetVersion = version;
                                Console.WriteLine($"[Adapter] Detected Protocol Version: {TargetVersion}");
                            }
                        }
                        
                        var protocolVersionOverrideField = multiplayerType.GetField("protocolVersionOverride", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                        if (protocolVersionOverrideField != null)
                        {
                            protocolVersionOverrideField.SetValue(null, TargetVersion);
                            Console.WriteLine($"[Adapter] Overrode Multiplayer.protocolVersionOverride with: {TargetVersion}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Adapter] Error reading protocolVersion via Reflection: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[Adapter] Warning: Stardew Valley.dll not found at {gameDllPath}.");
            }

            // 4. Initialize Game1 static state
            Console.WriteLine("[Adapter] Mocking Game1 static fields...");
            
            var serviceContainer = new GameServiceContainer();
            var headlessContent = new HeadlessContentManager(serviceContainer, "Content", steamDirPath);
            Game1.content = headlessContent;
            
            var gameInstance = (Game1)FormatterServices.GetUninitializedObject(typeof(Game1));
            Game1.game1 = gameInstance;

            try
            {
                var localMultiplayerType = typeof(Game1).Assembly.GetType("StardewValley.LocalMultiplayer");
                if (localMultiplayerType != null)
                {
                    var initMethod = localMultiplayerType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    initMethod?.Invoke(null, null);
                    Console.WriteLine("[Adapter] LocalMultiplayer initialized successfully.");

                    var staticVarHolderTypeField = localMultiplayerType.GetField("StaticVarHolderType", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var staticVarHolderType = staticVarHolderTypeField?.GetValue(null) as Type;
                    if (staticVarHolderType != null)
                    {
                        var holderInstance = Activator.CreateInstance(staticVarHolderType);
                        var staticVarHolderField = typeof(Game1).Assembly.GetType("StardewValley.InstanceGame")?.GetField("staticVarHolder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        staticVarHolderField?.SetValue(gameInstance, holderInstance);
                        Console.WriteLine("[Adapter] Successfully set staticVarHolder on gameInstance.");

                        var staticSetDefaultField = localMultiplayerType.GetField("StaticSetDefault", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                        var staticSetDefault = staticSetDefaultField?.GetValue(null) as Delegate;
                        staticSetDefault?.DynamicInvoke(holderInstance);
                        Console.WriteLine("[Adapter] Successfully initialized staticVarHolder defaults.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Adapter] Error setting up LocalMultiplayer/staticVarHolder: {ex}");
            }
            
            var locationsField = typeof(Game1).GetField("_locations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (locationsField != null)
            {
                var locList = new List<GameLocation>();
                locationsField.SetValue(gameInstance, locList);

                var farm = new Farm();
                farm.mapPath.Value = "Maps\\Farm";
                var loadedMapPathField = typeof(GameLocation).GetField("loadedMapPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                loadedMapPathField?.SetValue(farm, "Maps\\Farm");
                farm.name.Value = "Farm";
                farm.isAlwaysActive.Value = true;
                locList.Add(farm);
            }
            
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

            var farmLoc = Game1.getLocationFromName("Farm");
            if (multiplayer != null && farmLoc != null)
            {
                multiplayer.locationRoot(farmLoc);
            }

            Game1.netWorldState = new NetRoot<NetWorldState>(new NetWorldState());

            var programType = typeof(StardewValley.Program);
            var sdkField = programType.GetField("_sdk", BindingFlags.Static | BindingFlags.NonPublic);
            if (sdkField != null)
            {
                var nullSdkHelperType = typeof(StardewValley.SDKs.NullSDKHelper);
                var nullSdkHelper = Activator.CreateInstance(nullSdkHelperType);
                sdkField.SetValue(null, nullSdkHelper);
                Console.WriteLine("[Adapter] Successfully mocked Program._sdk to NullSDKHelper.");
            }

            try
            {
                var registerMethod = typeof(ItemRegistry).GetMethod("RegisterItemTypes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                registerMethod?.Invoke(null, null);
                Console.WriteLine("[Adapter] ItemRegistry types registered.");

                Game1.objectData = DataLoader.Objects(Game1.content);
                Game1.bigCraftableData = DataLoader.BigCraftables(Game1.content);
                Game1.weaponData = DataLoader.Weapons(Game1.content);
                Game1.toolData = DataLoader.Tools(Game1.content);
                Game1.pantsData = DataLoader.Pants(Game1.content);
                Game1.shirtData = DataLoader.Shirts(Game1.content);
                Game1.locationContextData = DataLoader.LocationContexts(Game1.content);
                CraftingRecipe.craftingRecipes = DataLoader.CraftingRecipes(Game1.content);
                CraftingRecipe.cookingRecipes = DataLoader.CookingRecipes(Game1.content);
                Console.WriteLine("[Adapter] Item databases and recipes loaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Adapter] Error initializing ItemRegistry/databases: {ex}");
                Game1.objectData ??= new Dictionary<string, StardewValley.GameData.Objects.ObjectData>();
                Game1.bigCraftableData ??= new Dictionary<string, StardewValley.GameData.BigCraftables.BigCraftableData>();
                Game1.weaponData ??= new Dictionary<string, StardewValley.GameData.Weapons.WeaponData>();
                Game1.toolData ??= new Dictionary<string, StardewValley.GameData.Tools.ToolData>();
                Game1.pantsData ??= new Dictionary<string, StardewValley.GameData.Pants.PantsData>();
                Game1.shirtData ??= new Dictionary<string, StardewValley.GameData.Shirts.ShirtData>();
                Game1.locationContextData ??= new Dictionary<string, LocationContextData>();
                CraftingRecipe.craftingRecipes ??= new Dictionary<string, string>();
                CraftingRecipe.cookingRecipes ??= new Dictionary<string, string>();
            }

            var host = new Farmer();
            host.Name = "Host";
            host.farmName.Value = "HeadlessFarm";
            host.UniqueMultiplayerID = 99999999L;
            host.isCustomized.Value = true;
            host.gameVersion = Game1.version ?? TargetVersion;
            host.teamRoot = new NetRoot<FarmerTeam>(new FarmerTeam());
            
            var playerField = typeof(Game1).GetField("_player", BindingFlags.Static | BindingFlags.NonPublic);
            if (playerField != null)
            {
                playerField.SetValue(null, host);
            }
            Game1.serverHost = new NetFarmerRoot(host);

            Game1.otherFarmers = new NetRootDictionary<long, Farmer>();
            Game1.otherFarmers.Serializer = SaveSerializer.GetSerializer(typeof(Farmer));

            LoadSavedFarmhands();
            Console.WriteLine("[Adapter] Game1 static fields mocked successfully!");
        }

        public void LoadSavedFarmhands()
        {
            if (!Directory.Exists(_savedFarmhandsPath))
            {
                Directory.CreateDirectory(_savedFarmhandsPath);
            }

            foreach (var file in Directory.GetFiles(_savedFarmhandsPath, "*.xml"))
            {
                try
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                        var farmer = serializer.Deserialize(stream) as Farmer;
                        if (farmer != null && farmer.UniqueMultiplayerID != 99999999L && farmer.UniqueMultiplayerID != 0)
                        {
                            _savedFarmerIds.Add(farmer.UniqueMultiplayerID);
                            var root = new NetFarmerRoot(farmer);
                            Game1.otherFarmers.Roots[farmer.UniqueMultiplayerID] = root;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Adapter] Error loading saved farmhand from {file}: {ex}");
                }
            }
        }

        public void SaveFarmhand(Farmer farmer)
        {
            if (!Directory.Exists(_savedFarmhandsPath))
            {
                Directory.CreateDirectory(_savedFarmhandsPath);
            }

            string filePath = Path.Combine(_savedFarmhandsPath, $"{farmer.UniqueMultiplayerID}.xml");
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                    serializer.Serialize(stream, farmer);
                }
                Console.WriteLine($"[Adapter] Successfully saved farmhand {farmer.Name} ({farmer.UniqueMultiplayerID}) to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Adapter] Error saving farmhand {farmer.Name}: {ex.Message}");
            }
        }

        public void SaveAllActiveFarmhands()
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

        public void ReloadFarmhandOnDisconnect(long playerId, Farmer? fallbackFarmer)
        {
            string filePath = Path.Combine(_savedFarmhandsPath, $"{playerId}.xml");
            if (File.Exists(filePath))
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        var serializer = SaveSerializer.GetSerializer(typeof(Farmer));
                        var farmer = serializer.Deserialize(stream) as Farmer;
                        if (farmer != null)
                        {
                            var root = new NetFarmerRoot(farmer);
                            Game1.otherFarmers.Roots[playerId] = root;
                            Console.WriteLine($"[Adapter] Successfully reloaded and re-registered farmhand {playerId} from disk.");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Adapter] Error reloading farmhand {playerId} on disconnect: {ex}");
                }
            }

            if (fallbackFarmer != null)
            {
                Game1.otherFarmers.Roots[playerId] = new NetFarmerRoot(fallbackFarmer);
                Console.WriteLine($"[Adapter] Re-registered disconnected farmer {playerId} from memory.");
            }
        }

        public List<Farmer> GetAvailableFarmhands()
        {
            var availableList = new List<Farmer>();
            
            foreach (var rootKvp in Game1.otherFarmers.Roots)
            {
                var fh = rootKvp.Value.Value;
                if (fh != null && fh.isCustomized.Value)
                {
                    Console.WriteLine($"[Adapter] Checking saved farmhand: Name={fh.Name}, ID={fh.UniqueMultiplayerID}, isCustomized={fh.isCustomized.Value}");
                    if (!_sessionManager.HasSession(fh.UniqueMultiplayerID))
                    {
                        availableList.Add(fh);
                        Console.WriteLine($"[Adapter] Adding saved farmhand to available list: {fh.Name} ({fh.UniqueMultiplayerID})");
                    }
                    else
                    {
                        Console.WriteLine($"[Adapter] Skipping active farmhand: {fh.Name} ({fh.UniqueMultiplayerID})");
                    }
                }
            }

            if (availableList.Count < 4)
            {
                long newId = 11111111L;
                while (Game1.otherFarmers.Roots.ContainsKey(newId) || availableList.Any(f => f.UniqueMultiplayerID == newId))
                {
                    newId += 1;
                }
                
                var farmhand = new Farmer();
                farmhand.UniqueMultiplayerID = newId;
                farmhand.isCustomized.Value = false;
                farmhand.gameVersion = Game1.version ?? TargetVersion;
                
                availableList.Add(farmhand);
                Console.WriteLine($"[Adapter] Added new farmhand slot with ID: {newId}");
            }

            return availableList;
        }

        public bool IsFarmerSaved(long playerId)
        {
            return _savedFarmerIds.Contains(playerId);
        }

        public void MarkFarmerAsSaved(long playerId)
        {
            _savedFarmerIds.Add(playerId);
        }

        public GameLocation GetLocation(string name, bool isStructure)
        {
            GameLocation? location = Game1.getLocationFromName(name, isStructure);
            if (location == null)
            {
                Console.WriteLine($"[Adapter] Location '{name}' not found on server. Instantiating on the fly...");
                if (name == "FarmHouse")
                {
                    location = new StardewValley.Locations.FarmHouse();
                }
                else
                {
                    location = new GameLocation();
                }
                location.mapPath.Value = "Maps\\" + name;
                var loadedMapPathField = typeof(GameLocation).GetField("loadedMapPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                loadedMapPathField?.SetValue(location, "Maps\\" + name);
                location.name.Value = name;
                location.isAlwaysActive.Value = true;
                
                var locList = typeof(Game1).GetField("_locations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(Game1.game1) as List<GameLocation>;
                locList?.Add(location);
            }
            return location;
        }

        public Multiplayer? GetMultiplayer()
        {
            return typeof(Game1).GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) as Multiplayer;
        }

        public byte[] GetHostBytes(long peerId)
        {
            return WriteObjectFullBytes(Game1.serverHost, peerId);
        }

        public byte[] GetTeamBytes(long peerId)
        {
            return WriteObjectFullBytes(Game1.player.teamRoot, peerId);
        }

        public byte[] GetWorldStateBytes(long peerId)
        {
            return WriteObjectFullBytes(Game1.netWorldState, peerId);
        }

        public byte[] GetLocationBytes(GameLocation location, long peerId)
        {
            var locRoot = new NetRoot<GameLocation>();
            locRoot.Set(location);
            return WriteObjectFullBytes(locRoot, peerId);
        }

        public void UpdateGameTicks()
        {
            Game1.ticks++;
            if (Game1.netWorldState != null)
            {
                Game1.netWorldState.Value.UpdateFromGame1();
            }

            try
            {
                var mp = GetMultiplayer();
                if (mp != null)
                {
                    mp.UpdateEarly();
                    mp.UpdateLate();
                }
            }
            catch (Exception)
            {
            }
        }

        public void ProcessIncomingMessage(IncomingMessage incomingMsg)
        {
            var mp = GetMultiplayer();
            mp?.processIncomingMessage(incomingMsg);
        }

        public bool IsClientBroadcastType(byte messageType)
        {
            var mp = GetMultiplayer();
            return mp?.isClientBroadcastType(messageType) ?? false;
        }

        public void CleanUpDisconnectedPlayer(long playerId)
        {
            var mp = GetMultiplayer();
            mp?.playerDisconnected(playerId);
        }

        public void ReadIncomingMessage(NetBufferReadStream stream, IncomingMessage msg)
        {
            MockLidgrenMessageUtils.ReadStreamToMessage(stream, msg);
        }

        public void WriteMessageToOutgoing(OutgoingMessage srcMsg, NetOutgoingMessage destMsg)
        {
            MockLidgrenMessageUtils.WriteMessage(srcMsg, destMsg);
        }

        public void BroadcastMessage(OutgoingMessage outMsg, NetServer netServer, NetConnection? excludeConnection = null)
        {
            if (netServer.Connections.Count == 0) return;
            
            var msg = netServer.CreateMessage();
            WriteMessageToOutgoing(outMsg, msg);
            
            List<NetConnection> targets = new List<NetConnection>();
            foreach (var conn in netServer.Connections)
            {
                if (conn != excludeConnection && conn.Status == NetConnectionStatus.Connected)
                {
                    targets.Add(conn);
                }
            }

            if (targets.Count > 0)
            {
                netServer.SendMessage(msg, targets, NetDeliveryMethod.ReliableOrdered, 0);
            }
        }

        public byte[] WriteFarmerRootFullBytes(NetRoot<Farmer> root, long peerId)
        {
            return WriteObjectFullBytes<Farmer>(root, peerId);
        }

        public byte[] WriteObjectFullBytes<T>(NetRoot<T> root, long peer) where T : class, INetObject<INetSerializable>
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
