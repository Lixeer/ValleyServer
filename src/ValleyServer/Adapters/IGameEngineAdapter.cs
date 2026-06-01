using System.Collections.Generic;
using Netcode;
using StardewValley;
using StardewValley.Network;
using Lidgren.Network;

namespace ValleyServer.Adapters
{
    public interface IGameEngineAdapter
    {
        string TargetVersion { get; }
        
        void Initialize(string steamDirPath, string gameDllPath);
        
        // Save/Load Storage
        void LoadSavedFarmhands();
        void SaveFarmhand(Farmer farmer);
        void SaveAllActiveFarmhands();
        void ReloadFarmhandOnDisconnect(long playerId, Farmer? fallbackFarmer);
        List<Farmer> GetAvailableFarmhands();
        bool IsFarmerSaved(long playerId);
        void MarkFarmerAsSaved(long playerId);

        // State Mapping & Location
        GameLocation GetLocation(string name, bool isStructure);
        byte[] GetHostBytes(long peerId);
        byte[] GetTeamBytes(long peerId);
        byte[] GetWorldStateBytes(long peerId);
        byte[] GetLocationBytes(GameLocation location, long peerId);
        byte[] WriteFarmerRootFullBytes(NetRoot<Farmer> root, long peerId);

        // Core Tick & Updates
        void UpdateGameTicks();
        void ProcessIncomingMessage(IncomingMessage incomingMsg);
        bool IsClientBroadcastType(byte messageType);
        void CleanUpDisconnectedPlayer(long playerId);

        // Lidgren Utilities
        void ReadIncomingMessage(NetBufferReadStream stream, IncomingMessage msg);
        void WriteMessageToOutgoing(OutgoingMessage srcMsg, NetOutgoingMessage destMsg);
        
        // Broadcast Helper
        void BroadcastMessage(OutgoingMessage outMsg, NetServer netServer, NetConnection? excludeConnection = null);
    }
}
