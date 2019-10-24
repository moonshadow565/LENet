using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LENet
{
    public enum ENetProtocolCommand
    {
        NONE = 0x00,
        ACKNOWLEDGE = 0x01,
        CONNECT = 0x02,
        VERIFY_CONNECT = 0x03,
        DISCONNECT = 0x04,
        PING = 0x05,
        SEND_RELIABLE = 0x06,
        SEND_UNRELIABLE = 0x07,
        SEND_FRAGMENT = 0x08,
        SEND_UNSEQUENCED = 0x09,
        BANDWIDTH_LIMIT = 0x0A,
        THROTTLE_CONFIGURE = 0x0B,
    }

    public abstract class ENetProtocol
    {
        public ENetCommandFlag Flags { get; set; }
        public byte ChannelID { get; set; }
        public ushort ReliableSequenceNumber { get; set; }
        public abstract byte Size { get; }
        public abstract ENetProtocolCommand Command { get; }
        
        public const byte BASE_SIZE = 4;

        protected abstract void ReadInternal(ENetBuffer reader);
        protected abstract void WriteInternal(ENetBuffer writer);

        private ENetProtocol() { }

        public static ENetProtocol Create(ENetBuffer reader)
        {
            if(BASE_SIZE > reader.BytesLeft)
            {
                return null;
            }

            byte command_flags = reader.ReadByte();
            var channel = reader.ReadByte();
            var reliableSequenceNumber = reader.ReadUInt16();

            ENetProtocol result = (byte)(command_flags & 0x0Fu) switch
            {
                (byte)ENetProtocolCommand.NONE => null,
                (byte)ENetProtocolCommand.ACKNOWLEDGE => new Acknowledge(),
                (byte)ENetProtocolCommand.CONNECT => new Connect(),
                (byte)ENetProtocolCommand.VERIFY_CONNECT => new VerifyConnect(),
                (byte)ENetProtocolCommand.DISCONNECT => new Disconnect(),
                (byte)ENetProtocolCommand.PING => new Ping(),
                (byte)ENetProtocolCommand.SEND_FRAGMENT => new Send.Fragment(),
                (byte)ENetProtocolCommand.SEND_RELIABLE => new Send.Reliable(),
                (byte)ENetProtocolCommand.SEND_UNRELIABLE => new Send.Unreliable(),
                (byte)ENetProtocolCommand.SEND_UNSEQUENCED => new Send.Unsequenced(),
                (byte)ENetProtocolCommand.BANDWIDTH_LIMIT => new BandwidthLimit(),
                (byte)ENetProtocolCommand.THROTTLE_CONFIGURE => new ThrottleConfigure(),
                _ => null,
            };

            if(result == null || (result.Size - BASE_SIZE) > reader.BytesLeft)
            {
                return null;
            }

            result.ChannelID = channel;
            result.Flags = (ENetCommandFlag)(command_flags & 0xF0);
            result.ReliableSequenceNumber = reliableSequenceNumber;
            result.ReadInternal(reader);

            return result;
        }

        public void Write(ENetBuffer writer)
        {
            writer.WriteByte((byte)((byte)Flags | (byte)(Command)));
            writer.WriteByte(ChannelID);
            writer.WriteUInt16(ReliableSequenceNumber);
            WriteInternal(writer);
        }

        public sealed class Acknowledge : ENetProtocol
        {
            public ushort ReceivedReliableSequenceNumber { get; set; }
            public ushort ReceivedSentTime { get; set; }
            public const byte SIZE = 4 + 4;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.ACKNOWLEDGE;

            protected override void ReadInternal(ENetBuffer reader)
            {
                ReceivedReliableSequenceNumber = reader.ReadUInt16();
                ReceivedSentTime = reader.ReadUInt16();
            }
            protected override void WriteInternal(ENetBuffer writer)
            {
                writer.WriteUInt16(ReceivedReliableSequenceNumber);
                writer.WriteUInt16(ReceivedSentTime);
            }
        }

        public sealed class Connect : ENetProtocol
        {
            public byte OutgoingPeerID { get; set; }
            public ushort MTU { get; set; }
            public uint WindowSize { get; set; }
            public uint ChannelCount { get; set; }
            public uint IncomingBandwidth { get; set; }
            public uint OutgoingBandwidth { get; set; }
            public uint PacketThrottleInterval { get; set; }
            public uint PacketThrottleAcceleration { get; set; }
            public uint PacketThrottleDeceleration { get; set; }
            public byte SessionID { get; set; }
            public const byte SIZE = 4 + 36;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.CONNECT;

            protected override void ReadInternal(ENetBuffer reader)
            {
                OutgoingPeerID = reader.ReadByte();
                reader.ReadByte();
                MTU = reader.ReadUInt16();
                WindowSize = reader.ReadUInt32();
                ChannelCount = reader.ReadUInt32();
                IncomingBandwidth = reader.ReadUInt32();
                OutgoingBandwidth = reader.ReadUInt32();
                PacketThrottleInterval = reader.ReadUInt32();
                PacketThrottleAcceleration = reader.ReadUInt32();
                PacketThrottleDeceleration = reader.ReadUInt32();
                SessionID = reader.ReadByte();
                reader.ReadBytes(3);
            }

            protected override void WriteInternal(ENetBuffer writer)
            {
                writer.WriteByte(OutgoingPeerID);
                writer.WriteByte(0);
                writer.WriteUInt16(MTU);
                writer.WriteUInt32(WindowSize);
                writer.WriteUInt32(ChannelCount);
                writer.WriteUInt32(IncomingBandwidth);
                writer.WriteUInt32(OutgoingBandwidth);
                writer.WriteUInt32(PacketThrottleInterval);
                writer.WriteUInt32(PacketThrottleAcceleration);
                writer.WriteUInt32(PacketThrottleDeceleration);
                writer.WriteByte(SessionID);
                writer.WriteByte(0);
                writer.WriteByte(0);
                writer.WriteByte(0);
            }
        }

        public sealed class VerifyConnect : ENetProtocol
        {
            public byte OutgoingPeerID { get; set; }
            public ushort MTU { get; set; }
            public uint WindowSize { get; set; }
            public uint ChannelCount { get; set; }
            public uint IncomingBandwidth { get; set; }
            public uint OutgoingBandwidth { get; set; }
            public uint PacketThrottleInterval { get; set; }
            public uint PacketThrottleAcceleration { get; set; }
            public uint PacketThrottleDeceleration { get; set; }

            public const byte SIZE = 4 + 32;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.VERIFY_CONNECT;

            protected override void ReadInternal(ENetBuffer reader)
            {
                OutgoingPeerID = reader.ReadByte();
                reader.ReadByte();
                MTU = reader.ReadUInt16();
                WindowSize = reader.ReadUInt32();
                ChannelCount = reader.ReadUInt32();
                IncomingBandwidth = reader.ReadUInt32();
                OutgoingBandwidth = reader.ReadUInt32();
                PacketThrottleInterval = reader.ReadUInt32();
                PacketThrottleAcceleration = reader.ReadUInt32();
                PacketThrottleDeceleration = reader.ReadUInt32();
            }

            protected override void WriteInternal(ENetBuffer writer)
            {
                writer.WriteByte(OutgoingPeerID);
                writer.WriteByte(0);
                writer.WriteUInt16(MTU);
                writer.WriteUInt32(WindowSize);
                writer.WriteUInt32(ChannelCount);
                writer.WriteUInt32(IncomingBandwidth);
                writer.WriteUInt32(OutgoingBandwidth);
                writer.WriteUInt32(PacketThrottleInterval);
                writer.WriteUInt32(PacketThrottleAcceleration);
                writer.WriteUInt32(PacketThrottleDeceleration);
            }
        }

        public sealed class BandwidthLimit : ENetProtocol
        {
            public uint IncomingBandwidth { get; set; }
            public uint OutgoingBandwidth { get; set; }

            public const byte SIZE = 4 + 8;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.BANDWIDTH_LIMIT;

            protected override void ReadInternal(ENetBuffer reader)
            {
                IncomingBandwidth = reader.ReadUInt32();
                OutgoingBandwidth = reader.ReadUInt32();
            }

            protected override void WriteInternal(ENetBuffer writer)
            {
                writer.WriteUInt32(IncomingBandwidth);
                writer.WriteUInt32(OutgoingBandwidth);
            }
        }

        public sealed class ThrottleConfigure : ENetProtocol
        {
            public uint PacketThrottleInterval { get; set; }
            public uint PacketThrottleAcceleration { get; set; }
            public uint PacketThrottleDeceleration { get; set; }

            public const byte SIZE = 4 + 12;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.THROTTLE_CONFIGURE;

            protected override void ReadInternal(ENetBuffer reader)
            {
                PacketThrottleInterval = reader.ReadUInt32();
                PacketThrottleAcceleration = reader.ReadUInt32();
                PacketThrottleDeceleration = reader.ReadUInt32();
            }

            protected override void WriteInternal(ENetBuffer writer)
            {
                writer.WriteUInt32(PacketThrottleInterval);
                writer.WriteUInt32(PacketThrottleAcceleration);
                writer.WriteUInt32(PacketThrottleDeceleration);
            }
        }

        public sealed class Disconnect : ENetProtocol
        {
            public uint Data { get; set; }

            public const byte SIZE = 4 + 4;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.DISCONNECT;

            protected override void ReadInternal(ENetBuffer reader)
            {
                Data = reader.ReadUInt32();
            }

            protected override void WriteInternal(ENetBuffer writer)
            {
                writer.WriteUInt32(Data);
            }
        }

        public sealed class Ping : ENetProtocol
        {
            public const byte SIZE = 4 + 0;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.PING;

            protected override void ReadInternal(ENetBuffer reader) { }
            protected override void WriteInternal(ENetBuffer writer) { }
        }

        public sealed class None : ENetProtocol
        {
            public const byte SIZE = 4 + 0;
            public override byte Size => SIZE;
            public override ENetProtocolCommand Command => ENetProtocolCommand.NONE;

            protected override void ReadInternal(ENetBuffer reader) { }
            protected override void WriteInternal(ENetBuffer writer) { }
        }

        public abstract class Send : ENetProtocol
        {
            public abstract ushort DataLength { get; set; }
            private Send() { }

            public sealed class Reliable : ENetProtocol
            {
                public ushort DataLength { get; set; }

                public const byte SIZE = 4 + 2;
                public override byte Size => SIZE;
                public override ENetProtocolCommand Command => ENetProtocolCommand.SEND_RELIABLE;

                protected override void ReadInternal(ENetBuffer reader)
                {
                    DataLength = reader.ReadUInt16();
                }
                protected override void WriteInternal(ENetBuffer writer)
                {
                    writer.WriteUInt16(DataLength);
                }
            }

            public sealed class Unreliable : ENetProtocol
            {
                public ushort UnreliableSequenceNumber { get; set; }
                public ushort DataLength { get; set; }

                public const byte SIZE = 4 + 4;
                public override byte Size => SIZE;
                public override ENetProtocolCommand Command => ENetProtocolCommand.SEND_UNRELIABLE;

                protected override void ReadInternal(ENetBuffer reader)
                {
                    UnreliableSequenceNumber = reader.ReadUInt16();
                    DataLength = reader.ReadUInt16();
                }
                protected override void WriteInternal(ENetBuffer writer)
                {
                    writer.WriteUInt16(UnreliableSequenceNumber);
                    writer.WriteUInt16(DataLength);
                }
            }

            public sealed class Unsequenced : ENetProtocol
            {
                public ushort UnsequencedGroup { get; set; }
                public ushort DataLength { get; set; }

                public const byte SIZE = 4 + 4;
                public override byte Size => SIZE;
                public override ENetProtocolCommand Command => ENetProtocolCommand.SEND_UNSEQUENCED;

                protected override void ReadInternal(ENetBuffer reader)
                {
                    UnsequencedGroup = reader.ReadUInt16();
                    DataLength = reader.ReadUInt16();
                }

                protected override void WriteInternal(ENetBuffer writer)
                {
                    writer.WriteUInt16(UnsequencedGroup);
                    writer.WriteUInt16(DataLength);
                }
            }

            public sealed class Fragment : ENetProtocol
            {
                public ushort StartSequenceNumber { get; set; }
                public ushort DataLength { get; set; }
                public uint FragmentCount { get; set; }
                public uint FragmentNumber { get; set; }
                public uint TotalLength { get; set; }
                public uint FragmentOffset { get; set; }

                public const byte SIZE = 4 + 20;
                public override byte Size => SIZE;
                public override ENetProtocolCommand Command => ENetProtocolCommand.SEND_FRAGMENT;

                protected override void ReadInternal(ENetBuffer reader)
                {
                    StartSequenceNumber = reader.ReadUInt16();
                    DataLength = reader.ReadUInt16();
                    FragmentCount = reader.ReadUInt32();
                    FragmentNumber = reader.ReadUInt32();
                    TotalLength = reader.ReadUInt32();
                    FragmentOffset = reader.ReadUInt32();
                }

                protected override void WriteInternal(ENetBuffer writer)
                {
                    writer.WriteUInt16(StartSequenceNumber);
                    writer.WriteUInt16(DataLength);
                    writer.WriteUInt32(FragmentCount);
                    writer.WriteUInt32(FragmentNumber);
                    writer.WriteUInt32(TotalLength);
                    writer.WriteUInt32(FragmentOffset);
                }
            }
        }
    }
}
