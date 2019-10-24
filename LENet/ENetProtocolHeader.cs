using System;


namespace LENet
{
    [Flags]
    public enum ENetCommandFlag : byte
    {
        NONE = 0,
        ACKNOWLEDGE = (1 << 7),
        UNSEQUENCED = (1 << 6),
        ACKNOWLEDGE_UNSEQUENCED = ACKNOWLEDGE | UNSEQUENCED,
    }

    public sealed class ENetProtocolHeader
    {
        public uint CheckSum { get; set; } = 0;
        public byte SessionID { get; set; } = 0;
        public byte PeerID { get; set; } = 0;
        public ushort? TimeSent { get; set; } = null;

        public const byte SIZE = 8;

        public static ENetProtocolHeader Create(ENetBuffer reader)
        {
            var result = new ENetProtocolHeader();

            if (reader.BytesLeft < (SIZE - 2))
            {
                return null;
            }

            result.CheckSum = reader.ReadUInt32();
            result.SessionID = reader.ReadByte();
            byte peerID_hasTimeSent = reader.ReadByte();
            result.PeerID = (byte)(peerID_hasTimeSent & 0x7F);

            if ((peerID_hasTimeSent & 0x80) == 0x80)
            {
                if(reader.BytesLeft < 2)
                {
                    return null;
                }

                result.TimeSent = reader.ReadUInt16();
            }

            return result;
        }

        public void Write(ENetBuffer writer)
        {
            writer.WriteUInt32(CheckSum);
            writer.WriteByte(SessionID);
            writer.WriteByte((byte)((PeerID) | (TimeSent != null ? 0x80u : 0x0u)));

            if (TimeSent != null)
            {
                writer.WriteUInt16((ushort)TimeSent);
            }
        }
    }
}
