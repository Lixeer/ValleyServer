using System;
using System.Threading;
using System.Diagnostics;
using Lidgren.Network;
using StardewValley;
using StardewValley.Network;
using ValleyServer.Core;
using ValleyServer.Adapters;

namespace ValleyServer.Services
{
    public class ServerController
    {
        private readonly ISessionManager _sessionManager;
        private readonly IGameEngineAdapter _gameEngine;
        private readonly PlayerManager _playerManager;
        private bool _running;

        public ServerController(
            ISessionManager sessionManager, 
            IGameEngineAdapter gameEngine, 
            PlayerManager playerManager)
        {
            _sessionManager = sessionManager;
            _gameEngine = gameEngine;
            _playerManager = playerManager;
        }

        public void Start(int port)
        {
            Console.WriteLine($"[ServerController] Configuring Lidgren NetServer on port {port}...");
            
            NetPeerConfiguration config = new NetPeerConfiguration("StardewValley");
            config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            config.Port = port;
            config.ConnectionTimeout = 30f;
            config.PingInterval = 5f;
            config.MaximumConnections = 8 * 2;
            config.MaximumTransmissionUnit = 1200;

            NetServer server = new NetServer(config);
            server.Start();
            Console.WriteLine($"[ServerController] NetServer started and listening on port {port}...");

            // Set Stardew Valley's global server reference to our HeadlessGameServer implementation
            Game1.server = new HeadlessGameServer(server, _sessionManager);

            var stopwatch = Stopwatch.StartNew();
            long lastTickTime = 0;
            const long msPerTick = 16; // ~60 ticks per second

            _running = true;
            while (_running)
            {
                NetIncomingMessage inc;
                while ((inc = server.ReadMessage()) != null)
                {
                    try
                    {
                        HandleIncomingMessage(inc, server);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ServerController] Exception processing network message: {ex}");
                    }
                    finally
                    {
                        server.Recycle(inc);
                    }
                }

                long currentTime = stopwatch.ElapsedMilliseconds;
                if (currentTime - lastTickTime >= msPerTick)
                {
                    lastTickTime = currentTime;
                    
                    // Update Game static ticks and multiplayer early/late updates
                    _gameEngine.UpdateGameTicks();

                    // Process outgoing messages queues
                    _playerManager.ProcessMessageQueue(server);
                }
                
                Thread.Sleep(1);
            }
        }

        public void Stop()
        {
            _running = false;
        }

        private void HandleIncomingMessage(NetIncomingMessage inc, NetServer server)
        {
            switch (inc.MessageType)
            {
                case NetIncomingMessageType.DiscoveryRequest:
                    Console.WriteLine($"[ServerController] DiscoveryRequest from {inc.SenderEndPoint}. Replying with protocol version {_gameEngine.TargetVersion}...");
                    NetOutgoingMessage response = server.CreateMessage();
                    response.Write(_gameEngine.TargetVersion);
                    response.Write("Headless Stardew Valley Server");
                    server.SendDiscoveryResponse(response, inc.SenderEndPoint);
                    break;

                case NetIncomingMessageType.ConnectionApproval:
                    Console.WriteLine($"[ServerController] ConnectionApproval from {inc.SenderEndPoint}. Approving...");
                    inc.SenderConnection.Approve();
                    break;

                case NetIncomingMessageType.StatusChanged:
                    var status = (NetConnectionStatus)inc.ReadByte();
                    string reason = inc.ReadString();
                    Console.WriteLine($"[ServerController] Status changed for {inc.SenderEndPoint}: {status} (Reason: {reason})");

                    if (status == NetConnectionStatus.Connected)
                    {
                        _playerManager.HandleClientConnected(inc.SenderConnection, server);
                    }
                    else if (status == NetConnectionStatus.Disconnected || status == NetConnectionStatus.None)
                    {
                        _playerManager.HandleClientDisconnected(inc.SenderConnection, server);
                    }
                    break;

                case NetIncomingMessageType.Data:
                    IncomingMessage incomingMsg = new IncomingMessage();
                    using (NetBufferReadStream stream = new NetBufferReadStream(inc))
                    {
                        while (inc.LengthBits - inc.Position >= 8)
                        {
                            _gameEngine.ReadIncomingMessage(stream, incomingMsg);
                            
                            if (incomingMsg.MessageType == 2)
                            {
                                _playerManager.HandlePlayerIntroduction(incomingMsg, inc.SenderConnection, server);
                            }
                            else if (incomingMsg.MessageType == 5)
                            {
                                _playerManager.HandlePlayerWarp(incomingMsg, inc.SenderConnection, server);
                            }
                            else
                            {
                                try
                                {
                                    _gameEngine.ProcessIncomingMessage(incomingMsg);
                                    
                                    // Check if the source farmer has completed customization and needs saving
                                    var farmer = incomingMsg.SourceFarmer;
                                    if (farmer != null && farmer.UniqueMultiplayerID != 99999999L && farmer.UniqueMultiplayerID != 0 && farmer.isCustomized.Value)
                                    {
                                        if (!_gameEngine.IsFarmerSaved(farmer.UniqueMultiplayerID))
                                        {
                                            Console.WriteLine($"[ServerController] Farmer {farmer.Name} ({farmer.UniqueMultiplayerID}) completed customization. Saving...");
                                            _gameEngine.SaveFarmhand(farmer);
                                            _gameEngine.MarkFarmerAsSaved(farmer.UniqueMultiplayerID);
                                        }
                                    }

                                    // Rebroadcast client broadcast messages to other clients
                                    if (_gameEngine.IsClientBroadcastType(incomingMsg.MessageType))
                                    {
                                        var outMsg = new OutgoingMessage(incomingMsg);
                                        _gameEngine.BroadcastMessage(outMsg, server, inc.SenderConnection);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[ServerController] Error processing data message {incomingMsg.MessageType}: {ex.Message}");
                                }
                            }
                        }
                    }
                    break;

                default:
                    break;
            }
        }
    }
}
