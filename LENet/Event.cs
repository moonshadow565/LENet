using System;

namespace LENet
{
    public enum EventType
    {
        NONE = 0,
        CONNECT = 1,
        DISCONNECT = 2,
        RECEIVE = 3,
    }

    public sealed class Event
    {
        public EventType Type { get; set; }
        public Peer Peer { get; set; }
        public byte ChannelID { get; set; }
        public uint Data { get; set; }
        public Packet Packet { get; set; }
    }
}
