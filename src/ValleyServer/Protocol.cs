#pragma warning disable SYSLIB0050

using System;
using System.IO;
using Lidgren.Network;
using StardewValley.Network;

namespace HeadlessServer
{
    /// <summary>Well-known Stardew Valley multiplayer message types.</summary>
    public static class MessageIds
    {
        public const byte PlayerDelta = 0;          // farmer root delta
        public const byte ServerIntroduction = 1;   // host -> joining farmhand
        public const byte PlayerIntroduction = 2;   // farmhand -> host
        public const byte LocationIntroduction = 3; // host -> farmhand
        public const byte PlayerWarp = 5;           // farmhand requests a warp
        public const byte LocationDelta = 6;
        public const byte AvailableFarmhands = 9;   // farmhand list / new-farmhand slots
        public const byte NewDaySync = 14;          // NetSynchronizer barrier/variable messages
        public const byte Disconnect = 19;
        public const byte StartNewDaySync = 30;     // NewDaySynchronizer.start notification
        public const byte NetReady = 31;            // ReadySynchronizer messages
        public const byte ForceKick = 25;
    }

    /// <summary>Names of the NewDaySynchronizer barriers and variables used by the day roll.</summary>
    public static class NewDaySyncNames
    {
        public const string FarmEvent = "farmEvent";
        public const string RemoveItemsFromWorld = "removeItemsFromWorld";
        public const string Saved = "saved";
        public const string Finished = "finished";
    }

    /// <summary>Names of the ReadySynchronizer checks used during sleep / end-of-day.</summary>
    public static class ReadyCheckNames
    {
        public const string Sleep = "sleep";
        public const string ReadyForSave = "ready_for_save";
        public const string Wakeup = "wakeup";
    }

    /// <summary>
    /// Pure helpers for inspecting and cloning the protocol messages the headless host
    /// exchanges with clients. Kept independent of Program so it can be unit-tested and
    /// reused by the transport and diagnostics layers.
    /// </summary>
    public static class ProtocolMessages
    {
        public static string DescribeOutgoingMessage(OutgoingMessage message)
        {
            try
            {
                if (message.MessageType == MessageIds.NewDaySync && message.Data.Count >= 2)
                {
                    byte subtype = Convert.ToByte(message.Data[0]);
                    string name = Convert.ToString(message.Data[1]) ?? "?";
                    return $"type={MessageIds.NewDaySync} subtype={subtype} name={name}";
                }
                if (message.MessageType == MessageIds.NetReady && message.Data.Count >= 2)
                    return $"type={MessageIds.NetReady} check={message.Data[0]} subtype={message.Data[1]}";
                return $"type={message.MessageType}";
            }
            catch (Exception ex)
            {
                return $"type={message.MessageType} decodeError={ex.Message}";
            }
        }

        public static string DescribeIncomingMessage(IncomingMessage message)
        {
            try
            {
                using var stream = new MemoryStream(message.Data, writable: false);
                using var reader = new BinaryReader(stream);
                if (message.MessageType == MessageIds.NewDaySync)
                {
                    byte subtype = reader.ReadByte();
                    string name = reader.ReadString();
                    return $"type={MessageIds.NewDaySync} subtype={subtype} name={name}";
                }
                if (message.MessageType == MessageIds.NetReady)
                {
                    string check = reader.ReadString();
                    byte subtype = reader.ReadByte();
                    return $"type={MessageIds.NetReady} check={check} subtype={subtype}";
                }
                return $"type={message.MessageType}";
            }
            catch (Exception ex)
            {
                return $"type={message.MessageType} decodeError={ex.Message}";
            }
        }

        /// <summary>
        /// Deep-copy an incoming message. The receive loop reuses a single IncomingMessage
        /// instance, so anything that must be processed later (e.g. by the overnight worker
        /// on another thread) has to be cloned first.
        /// </summary>
        public static IncomingMessage Clone(IncomingMessage source)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                new OutgoingMessage(source).Write(writer);
            stream.Position = 0;
            var clone = new IncomingMessage();
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            clone.Read(reader);
            return clone;
        }
    }
}
