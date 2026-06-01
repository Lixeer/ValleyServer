using Lidgren.Network;
using System.Collections.Generic;

namespace ValleyServer.Core
{
    public interface ISessionManager
    {
        void RegisterSession(long playerId, NetConnection connection);
        void RemoveSession(long playerId);
        NetConnection? GetConnection(long playerId);
        long? GetPlayerId(NetConnection connection);
        bool HasSession(long playerId);
        IReadOnlyDictionary<long, NetConnection> ActiveSessions { get; }
    }
}
