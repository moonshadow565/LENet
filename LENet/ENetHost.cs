using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace LENet
{
    public sealed partial class ENetHost : IDisposable
    {
        public const uint RECEIVE_BUFFER_SIZE = 256 * 1024;
        public const uint SEND_BUFFER_SIZE = 256 * 1024;
        // public const uint BANDWIDTH_THROTTLE_INTERVAL = 0x0FFFFFFFF;
        public const long BANDWIDTH_THROTTLE_INTERVAL = -1;
        public const ushort DEFAULT_MTU = 1400;
        public const ushort MINIMUM_MTU = 576;
        public const ushort MAXIMUM_MTU = 4096;
        public const ushort MINIMUM_WINDOW_SIZE = 4096;
        public const ushort MAXIMUM_WINDOW_SIZE = 32768;
        public const byte MINIMUM_CHANNEL_COUNT = 1;
        public const byte MAXIMUM_CHANNEL_COUNT = 255;
        public const byte MAXIMUM_PEER_ID = 0x7F;

        public Socket Socket { get; set; }
        public ENetAddress Address { get; set; }
        public uint IncomingBandwidth { get; set; }
        public uint OutgoingBandwidth { get; set; }
        public uint BandwidthThrottleEpoch { get; set; }
        public uint MTU { get; set; }
        public bool RecalculateBandwidthLimits { get; set; }
        public List<ENetPeer> Peers { get; set; } = new List<ENetPeer>();
        public uint PeerCount => (uint)Peers.Count;
        public uint ChannelLimit { get; set; }
        public uint ServiceTime { get; set; }
        public ENetList<ENetPeer> DispatchQueue { get; set; } = new ENetList<ENetPeer>();

        public uint TotalSentData { get; set; }
        public uint TotalSentPackets { get; set; }
        public uint TotalReceivedData { get; set; }
        public uint TotalReceivedPackets { get; set; }

        private uint _nextSessionID = 1;

        private readonly int _timeStart = Environment.TickCount;
        public uint GetTime() => (uint)(Environment.TickCount - _timeStart); 

        private ENetHost() { }

        public static ENetHost Create(ENetAddress? address, uint peerCount, uint incomingBandwith, uint outgoingBandwith)
        {
            if(peerCount > MAXIMUM_PEER_ID)
            {
                return null;
            }

            var host = new ENetHost
            {
                Peers = Utils.MakeList<ENetPeer>(peerCount),
                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP),
                ChannelLimit = MAXIMUM_CHANNEL_COUNT,
                IncomingBandwidth = incomingBandwith,
                OutgoingBandwidth = outgoingBandwith,
                MTU = DEFAULT_MTU,
            };

            if(address is ENetAddress addr)
            {
                host.Address = addr;
                try
                {
                    host.Socket.Bind(new IPEndPoint(addr.Host, addr.Port));
                }
                catch (Exception)
                {
                    host.Dispose();
                    return null;
                }
            }

            host.Socket.Blocking = false;
            host.Socket.EnableBroadcast = true;
            host.Socket.ReceiveBufferSize = (int)RECEIVE_BUFFER_SIZE;
            host.Socket.SendBufferSize = (int)SEND_BUFFER_SIZE;

            for (var i = 0; i < peerCount; i++)
            {
                host.Peers[i].Host = host;
                host.Peers[i].IncomingPeerID = (byte)i;
                host.Peers[i].Reset();
            }

            return host;
        }

        public void Dispose()
        {
            Socket.Dispose();
        }

        public ENetPeer Connect(ENetAddress address, uint channelCount)
        {
            channelCount = Math.Clamp(channelCount, MINIMUM_CHANNEL_COUNT, MAXIMUM_CHANNEL_COUNT);

            var currentPeer = Peers.Find((p) => p.State == ENetPeerState.DISCONNECTED);

            if(currentPeer == null)
            {
                return null;
            }

            currentPeer.Channels = Utils.MakeList<ENetChannel>(channelCount);
            currentPeer.State = ENetPeerState.CONNECTING;
            currentPeer.Address = address;
            currentPeer.SessionID = (byte)(_nextSessionID++ & 0xFFu);
            
            if(OutgoingBandwidth == 0)
            {
                currentPeer.WindowSize = MAXIMUM_WINDOW_SIZE;
            } 
            else
            {
                currentPeer.WindowSize = (OutgoingBandwidth / ENetPeer.WINDOW_SIZE_SCALE) * MINIMUM_WINDOW_SIZE;
            }
            currentPeer.WindowSize = Math.Clamp(currentPeer.WindowSize, MINIMUM_WINDOW_SIZE, MAXIMUM_WINDOW_SIZE);
            
            var command = new ENetProtocol.Connect
            {
                Flags = ENetCommandFlag.ACKNOWLEDGE,
                ChannelID = 0xFF,
                OutgoingPeerID = currentPeer.IncomingPeerID,
                MTU = currentPeer.MTU,
                WindowSize = currentPeer.WindowSize,
                ChannelCount = channelCount,
                IncomingBandwidth = IncomingBandwidth,
                OutgoingBandwidth = OutgoingBandwidth,
                PacketThrottleInterval = currentPeer.PacketThrottleInterval,
                PacketThrottleAcceleration = currentPeer.PacketThrottleAcceleration,
                PacketThrottleDeceleration = currentPeer.PacketThrottleDeceleration,
                SessionID = currentPeer.SessionID,
            };
            
            currentPeer.QueueOutgoingCommand(command, null, 0, 0);

            return currentPeer;
        }

        public void SetChannelLimit(uint channelLimit)
        {
            if(channelLimit == 0)
            {
                channelLimit = MAXIMUM_CHANNEL_COUNT;
            }
            ChannelLimit = Math.Clamp(channelLimit, MINIMUM_CHANNEL_COUNT, MAXIMUM_CHANNEL_COUNT);
        }

        public void Broadcast(byte channelID, ENetPacket packet)
        {
            foreach(var currentPeer in Peers)
            {
                if(currentPeer.State != ENetPeerState.CONNECTED)
                {
                    continue;
                }
                currentPeer.Send(channelID, packet);
            }
        }

        public void SetBandwidthLimit(uint incomingBandwidth, uint outgoingBandwidth)
        {
            IncomingBandwidth = incomingBandwidth;
            OutgoingBandwidth = outgoingBandwidth;
            RecalculateBandwidthLimits = true;
        }

        public void BandwidthThrottle()
        {
            uint timeCurrent = GetTime();
            uint elapsedTime = timeCurrent - BandwidthThrottleEpoch;

            if(elapsedTime < BANDWIDTH_THROTTLE_INTERVAL)
            {
                return;
            }

            uint peersTotal = 0;
            uint dataTotal = 0;
            foreach (var peer in Peers)
            {
                if(peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
                {
                    continue;
                }
                peersTotal++;
                dataTotal += peer.OutgoingDataTotal;
            }

            if(peersTotal == 0)
            {
                return;
            }

            uint peersRemaining = peersTotal;
            bool needsAdjustment = true;
            
            uint bandwidth = OutgoingBandwidth == 0 ? ~0u : (OutgoingBandwidth * elapsedTime) / 1000u;
            
            uint throttle = 0;
            
            while (peersRemaining > 0 && needsAdjustment)
            {
                needsAdjustment = false;

                if(dataTotal < bandwidth)
                {
                    throttle = ENetPeer.PACKET_THROTTLE_SCALE;
                }
                else
                {
                    throttle = (bandwidth * ENetPeer.PACKET_THROTTLE_SCALE) / dataTotal;
                }

                foreach (var peer in Peers)
                {
                    uint peerBandwidth = 0;

                    if ((peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
                        || peer.IncomingBandwidth == 0
                        || peer.OutgoingBandwidthThrottleEpoch == timeCurrent)
                    {
                        continue;
                    }

                    peerBandwidth = (peer.IncomingBandwidth * elapsedTime) / 1000;

                    if((throttle * peer.OutgoingDataTotal) / ENetPeer.PACKET_THROTTLE_SCALE <= peerBandwidth)
                    {
                        continue;
                    }

                    peer.PacketThrottleLimit = (peerBandwidth * ENetPeer.PACKET_THROTTLE_SCALE) / peer.OutgoingDataTotal;

                    if(peer.PacketThrottleLimit == 0)
                    {
                        peer.PacketThrottleLimit = 1;
                    }

                    if(peer.PacketThrottle > peer.PacketThrottleLimit)
                    {
                        peer.PacketThrottle = peer.PacketThrottleLimit;
                    }

                    peer.OutgoingBandwidthThrottleEpoch = timeCurrent;
                    
                    needsAdjustment = true;
                    peersRemaining--;
                    bandwidth -= peerBandwidth;
                    dataTotal -= peerBandwidth;
                }
            }

            if (peersRemaining > 0)
            {
                foreach (var peer in Peers)
                {
                    if ((peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
                        || peer.OutgoingBandwidthThrottleEpoch == timeCurrent)
                    {
                        continue;
                    }

                    peer.PacketThrottleLimit = throttle;

                    if(peer.PacketThrottle > peer.PacketThrottleLimit)
                    {
                        peer.PacketThrottle = peer.PacketThrottleLimit;
                    }
                }
            }

            if(RecalculateBandwidthLimits)
            {
                RecalculateBandwidthLimits = false;

                peersRemaining = peersTotal;
                bandwidth = IncomingBandwidth;
                needsAdjustment = true;

                uint bandwidthLimit = 0;
                if (bandwidth != 0)
                {
                    while (peersRemaining > 0 && needsAdjustment)
                    {
                        needsAdjustment = false;
                        bandwidthLimit = bandwidth / peersRemaining;

                        foreach (var peer in Peers)
                        {
                            if ((peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER) 
                                || peer.IncomingBandwidthThrottleEpoch == timeCurrent)
                            {
                                continue;
                            }

                            if (peer.OutgoingBandwidth > 0 
                                && peer.OutgoingBandwidth >= bandwidthLimit)
                            {
                                continue;
                            }

                            peer.IncomingBandwidthThrottleEpoch = timeCurrent;

                            needsAdjustment = true;
                            peersRemaining--;
                            bandwidth -= peer.OutgoingBandwidth;
                        }
                    }
                }

                foreach (var peer in Peers)
                {
                    if (peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
                    {
                        continue;
                    }

                    var command = new ENetProtocol.BandwidthLimit
                    {
                        Flags = ENetCommandFlag.ACKNOWLEDGE,
                        ChannelID = 0xFF,
                        OutgoingBandwidth = OutgoingBandwidth,
                    };

                    if (peer.IncomingBandwidthThrottleEpoch == timeCurrent)
                    {
                        command.IncomingBandwidth = peer.OutgoingBandwidth;
                    }
                    else
                    {
                        command.IncomingBandwidth = bandwidthLimit;
                    }

                    peer.QueueOutgoingCommand(command, null, 0, 0);
                }
            }

            BandwidthThrottleEpoch = timeCurrent;

            foreach (var peer in Peers)
            {
                peer.IncomingDataTotal = 0;
                peer.OutgoingDataTotal = 0;
            }
        }
    }
}
