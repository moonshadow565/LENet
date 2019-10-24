using System;

namespace LENet
{
    public sealed class ENetOutgoingCommand : ENetListNode<ENetOutgoingCommand>.Element
    {
        public ushort ReliableSequenceNumber { get; set; }
        public ushort UnreliableSequenceNumber { get; set; }
        public uint SentTime { get; set; }
        public uint RoundTripTimeout { get; set; }
        public uint RoundTripTimeoutLimit { get; set; }
        public uint FragmentOffset { get; set; }
        public ushort FragmentLength { get; set; }
        public ushort SendAttempts { get; set; }
        public ENetProtocol Command { get; set; }
        public ENetPacket Packet { get; set; }
    }
}
