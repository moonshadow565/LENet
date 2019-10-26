using System;

namespace LENet
{
    public sealed class Version
    {
        public readonly ushort MaxPeerID;

        public readonly uint ChecksumSizeSend;

        public readonly uint ChecksumSizeReceive;

        public readonly uint BandwidthThrottleInterval;

        public readonly uint PacketLossInterval;

        private readonly uint MaxHeaderSizeBase;

        public uint MaxHeaderSizeSend => ChecksumSizeSend + MaxHeaderSizeBase;

        public uint MaxHeaderSizeReceive => ChecksumSizeReceive + MaxHeaderSizeBase;

        private Version(ushort maxPeerID, uint checksumSizeSend, uint checksumSizeReceive, uint maxHeaderSizeBase, uint bandwidthThrottleInterval, uint packetLossInterval)
        {
            MaxPeerID = maxPeerID;
            ChecksumSizeSend = checksumSizeSend;
            ChecksumSizeReceive = checksumSizeReceive;
            MaxHeaderSizeBase = maxHeaderSizeBase;
            BandwidthThrottleInterval = bandwidthThrottleInterval;
            PacketLossInterval = packetLossInterval;
        }

        public static readonly Version Seasson12 = new Version(0x7FFF, 0, 0, 8, 1000, 10000);

        public static readonly Version Seasson34 = new Version(0x7F, 0, 0, 4, 0xFFFFFFFF, 0xFFFFFFFF);

        public static readonly Version Patch420 = new Version(0x7F, 4, 4, 4, 0xFFFFFFFF, 0xFFFFFFFF);

        public static readonly Version Seasson8_Client = new Version(0x7F, 8, 0, 4, 0xFFFFFFFF, 0xFFFFFFFF);

        public static readonly Version Seasson8_Server = new Version(0x7F, 0, 8, 4, 0xFFFFFFFF, 0xFFFFFFFF);
    }
}
