using System;

namespace LENet
{
    public enum ENetEventType
    {
        NONE = 0,
        CONNECT = 1,
        DISCONNECT = 2,
        RECEIVE = 3,
    }
    public sealed class ENetEvent
    {
        public ENetEventType Type { get; set; }
        public ENetPeer Peer { get; set; }
        public byte ChannelID { get; set; }
        public uint Data { get; set; }
        public ENetPacket Packet { get; set; }
    }
}
