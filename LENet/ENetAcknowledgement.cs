using System;

namespace LENet
{
    public class ENetAcknowledgement : ENetListNode<ENetAcknowledgement>.Element
    {
        public uint SentTime { get; set; }
        public ENetProtocol command { get; set; }
    }
}
