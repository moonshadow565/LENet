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
        public uint SessionID { get; set; } = 0;
        public ushort PeerID { get; set; } = 0;
        public ushort? TimeSent { get; set; } = null;

        public static ENetProtocolHeader Create(ENetBuffer reader, ENetVersion version)
        {
            var result = new ENetProtocolHeader();

            if ((version.MaxHeaderSizeReceive - 2) > reader.BytesLeft)
            {
                return null;
            }

            reader.Position += version.ChecksumSizeReceive;

            bool hasSentTime = false;

            switch(version)
            {
                case ENetVersion.Seasson12 _:
                    {
                        result.SessionID = reader.ReadUInt32();
                        ushort peerID = reader.ReadUInt16();
                        if ((peerID & 0x8000u) != 0)
                        {
                            hasSentTime = true;
                        }
                        result.PeerID = (ushort)(peerID & 0x7FFF);
                    }
                    break;
                case ENetVersion.Seasson34 _:
                case ENetVersion.Patch420 _:
                case ENetVersion.Seasson8 _:
                    {
                        result.SessionID = reader.ReadByte();
                        byte peerID = reader.ReadByte();
                        if ((peerID & 0x80) != 0)
                        {
                            hasSentTime = true;
                        }
                        result.PeerID = (ushort)(peerID & 0x7F);
                        break;
                    }
            }

            if (hasSentTime)
            {
                if(2 > reader.BytesLeft)
                {
                    return null;
                }

                result.TimeSent = reader.ReadUInt16();
            }

            return result;
        }

        public void Write(ENetBuffer writer, ENetVersion version)
        {
            writer.Position += version.ChecksumSizeSend;

            switch (version)
            {
                case ENetVersion.Seasson12 _:
                    {
                        writer.WriteUInt32(SessionID);
                        ushort peerID = (ushort)(PeerID | (TimeSent != null ? 0x8000u : 0u));
                        writer.WriteUInt16(peerID);
                    }
                    break;
                case ENetVersion.Seasson34 _:
                case ENetVersion.Patch420 _:
                case ENetVersion.Seasson8 _:
                    {
                        writer.WriteByte((byte)SessionID);
                        byte peerID = (byte)(PeerID | (TimeSent != null ? 0x80 : 0u));
                        writer.WriteByte(peerID);
                    }
                    break;
            }

            if (TimeSent != null)
            {
                writer.WriteUInt16((ushort)TimeSent);
            }
        }
    }
}
