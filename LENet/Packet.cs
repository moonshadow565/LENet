namespace LENet
{
    public sealed class Packet
    {
        public PacketFlags Flags { get; set; }
        public byte[] Data { get; set; }
        public uint DataLength => (uint)Data.Length;
    }
}
