using System.Collections.Generic;
using System.Collections.Concurrent;
using Lidgren.Network;

namespace ValleyServer.Core
{
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<long, NetConnection> _sessions = new ConcurrentDictionary<long, NetConnection>();

        public void RegisterSession(long playerId, NetConnection connection)
        {
            _sessions[playerId] = connection;
        }

        public void RemoveSession(long playerId)
        {
            _sessions.TryRemove(playerId, out _);
        }

        public NetConnection? GetConnection(long playerId)
        {
            _sessions.TryGetValue(playerId, out var connection);
            return connection;
        }

        public long? GetPlayerId(NetConnection connection)
        {
            foreach (var kvp in _sessions)
            {
                if (kvp.Value == connection)
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        public bool HasSession(long playerId)
        {
            return _sessions.ContainsKey(playerId);
        }

        public IReadOnlyDictionary<long, NetConnection> ActiveSessions => _sessions;
    }
}
