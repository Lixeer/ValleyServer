#pragma warning disable SYSLIB0050

using System;
using System.Collections.Generic;
using Lidgren.Network;
using StardewValley;
using StardewValley.Network;

namespace HeadlessServer
{
    /// <summary>
    /// IGameServer adapter for the headless dedicated host. Most operations are no-ops
    /// because the host is entirely simulated and never runs a UI/rendering server. The
    /// two live operations are outbound message delivery (mapped to the Lidgren server and
    /// its client connection table) and draining the deferred overnight message queue that
    /// the overnight worker owns.
    /// </summary>
    public class HeadlessGameServer : IGameServer
    {
        private NetServer _netServer;
        private Dictionary<long, NetConnection> _clientConnections;

        public HeadlessGameServer(NetServer netServer, Dictionary<long, NetConnection> clientConnections)
        {
            _netServer = netServer;
            _clientConnections = clientConnections;
        }

        public int connectionsCount => _netServer.Connections.Count;

        public BandwidthLogger BandwidthLogger => null;
        public bool LogBandwidth { get => false; set {} }

        public string getInviteCode() => "";
        public string getUserName(long farmerId) => "Player";
        public void setPrivacy(ServerPrivacy privacy) {}
        public void stopServer() {}
        public void receiveMessages()
        {
            Program.ProcessDeferredOvernightMessages();
        }
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
        public bool whenGameAvailable(Action action, Func<bool> customAvailabilityCheck = null) { action(); return true; }
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
            if (_clientConnections.TryGetValue(peerId, out var conn))
            {
                if (message.MessageType is 14 or 30 or 31)
                {
                    Console.WriteLine($"[Protocol][send] thread={Environment.CurrentManagedThreadId} peer={peerId} {ProtocolMessages.DescribeOutgoingMessage(message)}");
                }
                var msg = _netServer.CreateMessage();
                MockLidgrenMessageUtils.WriteMessage(message, msg);
                _netServer.SendMessage(msg, conn, NetDeliveryMethod.ReliableOrdered);
            }
            else if (message.MessageType is 14 or 30 or 31)
            {
                Console.WriteLine($"[Protocol][send-missed] peer={peerId} type={message.MessageType}; no mapped connection");
            }
        }

        public void sendMessage(long peerId, byte messageType, Farmer sourceFarmer, params object[] data)
        {
            this.sendMessage(peerId, new OutgoingMessage(messageType, sourceFarmer, data));
        }
    }
}
