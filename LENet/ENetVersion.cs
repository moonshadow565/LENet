using System;
using System.Collections.Generic;
using System.Text;

namespace LENet
{
    public abstract class ENetVersion
    {
        public abstract ushort MaxPeerID { get; }

        public abstract uint ChecksumSizeSend { get; }

        public abstract uint ChecksumSizeReceive { get; }

        protected abstract uint MaxHeaderSizeBase { get; }

        public uint MaxHeaderSizeSend => ChecksumSizeSend + MaxHeaderSizeBase;

        public uint MaxHeaderSizeReceive => ChecksumSizeReceive + MaxHeaderSizeBase;

        private ENetVersion() { }

        public sealed class Seasson12 : ENetVersion
        {
            public override ushort MaxPeerID => 0x7FFF;
            protected override uint MaxHeaderSizeBase => 8;

            public override uint ChecksumSizeSend => 0;

            public override uint ChecksumSizeReceive => 0;
        }

        public sealed class Seasson34 : ENetVersion
        {
            public override ushort MaxPeerID => 0x7F;
            protected override uint MaxHeaderSizeBase => 4;

            public override uint ChecksumSizeSend => 0;

            public override uint ChecksumSizeReceive => 0;
        }

        public sealed class Patch420 : ENetVersion
        {
            public override ushort MaxPeerID => 0x7F;
            protected override uint MaxHeaderSizeBase => 4;

            public override uint ChecksumSizeSend => 4;

            public override uint ChecksumSizeReceive => 4;
        }

        public sealed class Seasson8 : ENetVersion
        {
            public override ushort MaxPeerID => 0x7F;
            protected override uint MaxHeaderSizeBase => 4;

            public override uint ChecksumSizeSend { get; }

            public override uint ChecksumSizeReceive { get; }

            public Seasson8(bool hasChecksumSend, bool hasChecksumReceive)
            {
                ChecksumSizeSend = hasChecksumSend ? 8u : 0u;
                ChecksumSizeReceive = hasChecksumReceive ? 8u : 0u;
            }
        }
    }
}
