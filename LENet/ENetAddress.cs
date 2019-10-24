using System;
using System.Net;

namespace LENet
{
    public struct ENetAddress
    {
        public const uint Any = 0u;
        public const uint Broadcast = 0xFFFFFFFF;
        public uint Host { get; set; }
        public ushort Port { get; set; }

        public ENetAddress(IPEndPoint ipEndPoint)
        {
            Host = (uint)ipEndPoint.Address.Address;
            Port = (ushort)ipEndPoint.Port;
        }

        public ENetAddress(uint host, ushort port) : this(new IPEndPoint(host, port)) { }
        public ENetAddress(string host, ushort port) : this(new IPEndPoint(IPAddress.Parse(host), port)) { }
    }
}
