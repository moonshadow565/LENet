using System;
using System.Collections;

namespace LENet
{
    public sealed class ENetIncomingCommand : ENetListNode<ENetIncomingCommand>.Element
    {
        public ushort ReliableSequenceNumber { get; set; }
        public ushort UnreliableSequenceNumber { get; set; }
        public ENetProtocol Command { get; set; } = new ENetProtocol.None();
        public uint FragmentCount { get; set; }
        public uint FragmentsRemaining { get; set; }
        public BitArray Fragments { get; set; } = new BitArray(0);
        public ENetPacket Packet { get; set; } = null;

        public ENetIncomingCommand() { }
    }
}
