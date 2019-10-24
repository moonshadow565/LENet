using System;

namespace LENet
{
    public sealed class ENetChannel
    {
        public ushort OutgoingReliableSequenceNumber { get; set; } = 0;
        public ushort OutgoingUnreliableSequenceNumber { get; set; } = 0;
        public ushort UsedReliableWindows { get; set; } = 0;
        public ushort[] ReliableWindows { get; set; } = new ushort[ENetPeer.RELIABLE_WINDOWS];
        public ushort IncomingReliableSequenceNumber { get; set; } = 0;
        public ushort IncomingUnreliableSequenceNumber { get; set; } = 0;
        public ENetList<ENetIncomingCommand> IncomingReliableCommands { get; set; } = new ENetList<ENetIncomingCommand>();
        public ENetList<ENetIncomingCommand> IncomingUnreliableCommands { get; set; } = new ENetList<ENetIncomingCommand>();

        public ENetChannel() { }
    }
}
