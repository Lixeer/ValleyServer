#pragma warning disable SYSLIB0050

using System;
using System.Reflection;
using Lidgren.Network;
using StardewValley.Network;

namespace HeadlessServer
{
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
            if (writeMessageMethod == null)
                throw new MissingMethodException(typeof(LidgrenMessageUtils).FullName, "WriteMessage");
            writeMessageMethod.Invoke(null, new object[] { srcMsg, destMsg });
        }

        public static void ReadStreamToMessage(NetBufferReadStream stream, IncomingMessage msg)
        {
            if (readStreamToMessageMethod == null)
                throw new MissingMethodException(typeof(LidgrenMessageUtils).FullName, "ReadStreamToMessage");
            readStreamToMessageMethod.Invoke(null, new object[] { stream, msg });
        }
    }
}
