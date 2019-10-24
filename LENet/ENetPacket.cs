using System;

namespace LENet
{
    [Flags]
    public enum ENetPacketFlags
    {
        Reliable = (1 << 7),
        Unsequenced = (1 << 6),
        ReliableUnsequenced = Reliable | Unsequenced,
        None = 0,
    }

    public sealed class ENetPacket
    {
        public ENetPacketFlags Flags { get; set; }
        public byte[] Data { get; set; }
        public uint DataLength => (uint)Data.Length;
    }
}
