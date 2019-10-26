using System;

namespace LENet
{
    [Flags]
    public enum PacketFlags
    {
        Reliable = (1 << 7),
        Unsequenced = (1 << 6),
        ReliableUnsequenced = Reliable | Unsequenced,
        None = 0,
    }

    public sealed class Packet
    {
        public PacketFlags Flags { get; set; }
        public byte[] Data { get; set; }
        public uint DataLength => (uint)Data.Length;
    }
}
