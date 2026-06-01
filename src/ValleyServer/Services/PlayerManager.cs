using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Lidgren.Network;
using StardewValley;
using StardewValley.Network;
using StardewValley.SaveSerialization;
using ValleyServer.Core;
using ValleyServer.Adapters;

namespace ValleyServer.Services
{
    public class PlayerManager
    {
        private readonly ISessionManager _sessionManager;
        private readonly IGameEngineAdapter _gameEngine;

        public PlayerManager(ISessionManager sessionManager, IGameEngineAdapter gameEngine)
        {
            _sessionManager = sessionManager;
            _gameEngine = gameEngine;
        }

        public void HandleClientConnected(NetConnection connection, NetServer server)
        {
            Console.WriteLine($"[PlayerManager] Client {connection.RemoteEndPoint} connected successfully. Preparing and sending available farmhands...");
            
            var availableList = _gameEngine.GetAvailableFarmhands();

            byte[] payloadBytes;
            using (var memStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memStream))
                {
                    writer.Write(1); // year
                    writer.Write(0); // season (Spring)
                    writer.Write(1); // day
                    writer.Write((byte)availableList.Count); // available farmhands count
                    
                    foreach (var fh in availableList)
                    {
                        var farmhandRoot = new NetFarmerRoot(fh);
                        farmhandRoot.WriteFull(writer);
                    }
                    
                    payloadBytes = memStream.ToArray();
                }
            }

            var outgoingMsg = new OutgoingMessage(9, Game1.player.UniqueMultiplayerID, new object[] { payloadBytes });
            var netOutgoingMsg = server.CreateMessage();
            _gameEngine.WriteMessageToOutgoing(outgoingMsg, netOutgoingMsg);
            server.SendMessage(netOutgoingMsg, connection, NetDeliveryMethod.ReliableOrdered);
            Console.WriteLine("[PlayerManager] Sent available farmhands (Message 9) to client!");
        }

        public void HandleClientDisconnected(NetConnection connection, NetServer server)
        {
            Console.WriteLine($"[PlayerManager] Client {connection.RemoteEndPoint} disconnected. Saving all active farmhands...");
            _gameEngine.SaveAllActiveFarmhands();

            long? idToRemove = _sessionManager.GetPlayerId(connection);
            if (idToRemove.HasValue)
            {
                long playerId = idToRemove.Value;
                _sessionManager.RemoveSession(playerId);
                Console.WriteLine($"[PlayerManager] Removed mapping for player {playerId}");

                // Clean up player in otherFarmers
                Game1.otherFarmers.TryGetValue(playerId, out var disconnectedFarmer);
                _gameEngine.CleanUpDisconnectedPlayer(playerId);

                // Restore/reload the farmhand in Game1.otherFarmers from disk
                _gameEngine.ReloadFarmhandOnDisconnect(playerId, disconnectedFarmer);

                // Notify other clients about the disconnection
                if (disconnectedFarmer != null)
                {
                    var discMsg = new OutgoingMessage(19, disconnectedFarmer);
                    foreach (var connKvp in _sessionManager.ActiveSessions)
                    {
                        if (connKvp.Key != playerId)
                        {
                            var msg = server.CreateMessage();
                            _gameEngine.WriteMessageToOutgoing(discMsg, msg);
                            server.SendMessage(msg, connKvp.Value, NetDeliveryMethod.ReliableOrdered);
                        }
                    }
                    Console.WriteLine($"[PlayerManager] Broadcasted player {playerId} disconnect (Message 19) to remaining clients.");
                }
            }
        }

        public void HandlePlayerIntroduction(IncomingMessage incomingMsg, NetConnection senderConnection, NetServer server)
        {
            Console.WriteLine("[PlayerManager] Received PlayerIntroduction (Message 2) from client!");
            
            var clientFarmerRoot = new NetFarmerRoot();
            clientFarmerRoot.ReadConnectionPacket(incomingMsg.Reader);
            var clientFarmer = clientFarmerRoot.Value;
            long newClientId = clientFarmer.UniqueMultiplayerID;
            Console.WriteLine($"[PlayerManager] Client requested farmhand ID: {newClientId}, Name: {clientFarmer.Name}");

            // Register client farmhand
            Game1.otherFarmers.Roots[newClientId] = clientFarmerRoot;

            // Map connection
            _sessionManager.RegisterSession(newClientId, senderConnection);
            Console.WriteLine($"[PlayerManager] Mapped connection for player {newClientId}");

            // Send ServerIntroduction (Message 1)
            Console.WriteLine("[PlayerManager] Sending ServerIntroduction (Message 1)...");
            byte[] hostBytes = _gameEngine.GetHostBytes(newClientId);
            byte[] teamBytes = _gameEngine.GetTeamBytes(newClientId);
            byte[] worldStateBytes = _gameEngine.GetWorldStateBytes(newClientId);

            var introMsg = new OutgoingMessage(1, Game1.player.UniqueMultiplayerID, new object[] { hostBytes, teamBytes, worldStateBytes });
            var netIntroMsg = server.CreateMessage();
            _gameEngine.WriteMessageToOutgoing(introMsg, netIntroMsg);
            server.SendMessage(netIntroMsg, senderConnection, NetDeliveryMethod.ReliableOrdered);
            Console.WriteLine("[PlayerManager] Sent ServerIntroduction!");

            // Send LocationIntroduction (Message 3) for "Farm" location with force_current = true
            Console.WriteLine("[PlayerManager] Sending LocationIntroduction (Message 3)...");
            var location = Game1.getLocationFromName("Farm");
            byte[] locationBytes = _gameEngine.GetLocationBytes(location, newClientId);

            var locMsg = new OutgoingMessage(3, Game1.player.UniqueMultiplayerID, new object[] { true, locationBytes });
            var netLocMsg = server.CreateMessage();
            _gameEngine.WriteMessageToOutgoing(locMsg, netLocMsg);
            server.SendMessage(netLocMsg, senderConnection, NetDeliveryMethod.ReliableOrdered);
            Console.WriteLine("[PlayerManager] Sent LocationIntroduction!");

            // Introduce new client to existing clients, and vice versa
            foreach (var rootKvp in Game1.otherFarmers.Roots)
            {
                long otherId = rootKvp.Key;
                if (otherId != newClientId && otherId != 99999999L && otherId != 0)
                {
                    var otherConn = _sessionManager.GetConnection(otherId);
                    if (otherConn != null)
                    {
                        // 1. Send new client's introduction to existing client (otherId)
                        Console.WriteLine($"[PlayerManager] Introducing new player {newClientId} to existing player {otherId}...");
                        byte[] newClientBytes = _gameEngine.WriteFarmerRootFullBytes(clientFarmerRoot, otherId);
                        var introToExisting = new OutgoingMessage(2, clientFarmer, new object[] { "Player", newClientBytes });
                        var netMsgToExisting = server.CreateMessage();
                        _gameEngine.WriteMessageToOutgoing(introToExisting, netMsgToExisting);
                        server.SendMessage(netMsgToExisting, otherConn, NetDeliveryMethod.ReliableOrdered);

                        // 2. Send existing client's introduction to new client (newClientId)
                        Console.WriteLine($"[PlayerManager] Introducing existing player {otherId} to new player {newClientId}...");
                        byte[] existingClientBytes = _gameEngine.WriteFarmerRootFullBytes(rootKvp.Value, newClientId);
                        var introToNew = new OutgoingMessage(2, rootKvp.Value.Value, new object[] { "Player", existingClientBytes });
                        var netMsgToNew = server.CreateMessage();
                        _gameEngine.WriteMessageToOutgoing(introToNew, netMsgToNew);
                        server.SendMessage(netMsgToNew, senderConnection, NetDeliveryMethod.ReliableOrdered);
                    }
                }
            }
        }

        public void HandlePlayerWarp(IncomingMessage incomingMsg, NetConnection senderConnection, NetServer server)
        {
            try
            {
                short x = incomingMsg.Reader.ReadInt16();
                short y = incomingMsg.Reader.ReadInt16();
                string name = incomingMsg.Reader.ReadString();
                byte flags = incomingMsg.Reader.ReadByte();
                bool isStructure = (flags & 1) != 0;
                bool needsLocationInfo = (flags & 4) != 0;

                var farmer = incomingMsg.SourceFarmer;
                if (farmer != null && needsLocationInfo)
                {
                    GameLocation location = _gameEngine.GetLocation(name, isStructure);
                    farmer.currentLocation = location;
                    farmer.Position = new Vector2(x * 64, y * 64 - (farmer.Sprite.getHeight() - 32) + 16);

                    byte[] locationBytes = _gameEngine.GetLocationBytes(location, farmer.UniqueMultiplayerID);

                    var locMsg = new OutgoingMessage(3, Game1.player.UniqueMultiplayerID, new object[] { false, locationBytes });
                    var netLocMsg = server.CreateMessage();
                    _gameEngine.WriteMessageToOutgoing(locMsg, netLocMsg);
                    server.SendMessage(netLocMsg, senderConnection, NetDeliveryMethod.ReliableOrdered);
                    Console.WriteLine($"[PlayerManager] Sent warp location info for {name} to client!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PlayerManager] Error processing warp: {ex.Message}");
            }
        }

        public void ProcessMessageQueue(NetServer server)
        {
            // Forward queued messages for other farmers
            foreach (var farmer in Game1.otherFarmers.Values)
            {
                if (farmer.messageQueue.Count > 0)
                {
                    var conn = _sessionManager.GetConnection(farmer.UniqueMultiplayerID);
                    if (conn != null)
                    {
                        foreach (var outMsg in farmer.messageQueue)
                        {
                            var msg = server.CreateMessage();
                            _gameEngine.WriteMessageToOutgoing(outMsg, msg);
                            server.SendMessage(msg, conn, NetDeliveryMethod.ReliableOrdered);
                        }
                        farmer.messageQueue.Clear();
                    }
                }
            }

            // Forward queued messages for host (broadcast to all)
            if (Game1.player != null && Game1.player.messageQueue.Count > 0)
            {
                foreach (var outMsg in Game1.player.messageQueue)
                {
                    foreach (var connKvp in _sessionManager.ActiveSessions)
                    {
                        var msg = server.CreateMessage();
                        _gameEngine.WriteMessageToOutgoing(outMsg, msg);
                        server.SendMessage(msg, connKvp.Value, NetDeliveryMethod.ReliableOrdered);
                    }
                }
                Game1.player.messageQueue.Clear();
            }
        }
    }
}
