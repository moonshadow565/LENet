using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace LENet
{
    public sealed partial class ENetHost : IDisposable
    {
        private static bool ENET_TIME_LESS(uint a, uint b) { return a - b >= 86400000; }
        private static uint ENET_TIME_DIFFERENCE(uint a, uint b) { return ENET_TIME_LESS(a, b) ? b - a : a - b; }

        private int DispatchIncomingCommands(ENetEvent evnt)
        {
            while (!DispatchQueue.Empty)
            {
                var peer = DispatchQueue.Begin.Remove().Value;
                
                peer.NeedsDispatch = false;

                switch (peer.State)
                {
                    case ENetPeerState.CONNECTION_PENDING:
                    case ENetPeerState.CONNECTION_SUCCEEDED:
                        peer.State = ENetPeerState.CONNECTED;

                        evnt.Type = ENetEventType.CONNECT;
                        evnt.Peer = peer;

                        return 1;

                    case ENetPeerState.ZOMBIE:
                        RecalculateBandwidthLimits = true;

                        evnt.Type = ENetEventType.DISCONNECT;
                        evnt.Peer = peer;
                        evnt.Data = peer.DisconnectData;

                        peer.Reset();

                        return 1;

                    case ENetPeerState.CONNECTED:
                        if (peer.DispatchedCommands.Empty)
                        {
                            continue;
                        }

                        evnt.Packet = peer.Recieve(out byte channelID);

                        if (evnt.Packet == null)
                        {
                            continue;
                        }

                        evnt.ChannelID = channelID;
                        evnt.Type = ENetEventType.RECEIVE;
                        evnt.Peer = peer;

                        if (!peer.DispatchedCommands.Empty)
                        {
                            peer.NeedsDispatch = true;
                            
                            DispatchQueue.End.Insert(peer.Node);
                        }

                        return 1;
                }
            }

            return 0;
        }

        private void DispatchState(ENetPeer peer, ENetPeerState state)
        {
            peer.State = state;

            if (!peer.NeedsDispatch)
            {
                DispatchQueue.End.Insert(peer.Node);

                peer.NeedsDispatch = true;
            }
        }

        private void NotifyConnect(ENetPeer peer, ENetEvent evnt)
        {
            RecalculateBandwidthLimits = true;

            if (evnt != null)
            {
                peer.State = ENetPeerState.CONNECTED;

                evnt.Type = ENetEventType.CONNECT;
                evnt.Peer = peer;
            }
            else
            {
                DispatchState(peer, peer.State == ENetPeerState.CONNECTING ? ENetPeerState.CONNECTION_SUCCEEDED : ENetPeerState.CONNECTION_PENDING);
            }
        }

        private void NotifyDisconnect(ENetPeer peer, ENetEvent evnt)
        {
            if (peer.State >= ENetPeerState.CONNECTION_PENDING)
            {
                RecalculateBandwidthLimits = true;
            }

            if (peer.State != ENetPeerState.CONNECTING && peer.State < ENetPeerState.CONNECTION_SUCCEEDED)
            {
                peer.Reset();
            }
            else if (evnt != null)
            {
                evnt.Type = ENetEventType.DISCONNECT;
                evnt.Peer = peer;
                evnt.Data = 0;

                peer.Reset();
            }
            else
            {
                DispatchState(peer, ENetPeerState.ZOMBIE);
            }
        }

        private static void RemoveSentUnreliableCommands(ENetPeer peer)
        {
            peer.SentUnreliableCommands.Clear();
        }

        private static ENetProtocolCommand RemoveSentReliableCommand(ENetPeer peer, ushort reliableSequenceNumber, byte channelID)
        {
            ENetListNode<ENetOutgoingCommand> currentCommand;
            ENetOutgoingCommand outgoingCommand = null;
            bool wasSent = true;

            for (currentCommand = peer.SentReliableCommands.Begin;
                 currentCommand != peer.SentReliableCommands.End;
                 currentCommand = currentCommand.Next)
            {
                outgoingCommand = currentCommand.Value;

                if (outgoingCommand.ReliableSequenceNumber == reliableSequenceNumber
                    && outgoingCommand.Command.ChannelID == channelID)
                {
                    break;
                }
            }

            if (currentCommand == peer.SentReliableCommands.End)
            {
                for (currentCommand = peer.OutgoingReliableCommands.Begin;
                     currentCommand != peer.OutgoingReliableCommands.End;
                     currentCommand = currentCommand.Next)
                {
                    outgoingCommand = currentCommand.Value;

                    if (outgoingCommand.SendAttempts < 1)
                    {
                        return ENetProtocolCommand.NONE;
                    }

                    if (outgoingCommand.ReliableSequenceNumber == reliableSequenceNumber
                        && outgoingCommand.Command.ChannelID == channelID)
                    {
                        break;
                    }
                }

                if (currentCommand == peer.OutgoingReliableCommands.End)
                {
                    return ENetProtocolCommand.NONE;
                }

                wasSent = false;
            }

            if (channelID < peer.ChannelCount)
            {
                var channel = peer.Channels[channelID];
                ushort reliableWindow = (ushort)(reliableSequenceNumber / ENetPeer.RELIABLE_WINDOW_SIZE);
                if (channel.ReliableWindows[reliableWindow] > 0)
                {
                    channel.ReliableWindows[reliableWindow]--;
                    if (channel.ReliableWindows[reliableWindow] == 0)
                    {
                        channel.UsedReliableWindows &= (ushort)~(1u << reliableWindow);
                    }
                }
            }

            var commandNumber = outgoingCommand.Command.Command;
            outgoingCommand.Node.Remove();

            if (outgoingCommand.Packet != null)
            {
                if (wasSent)
                {
                    peer.ReliableDataInTransit -= outgoingCommand.FragmentLength;
                }
            }

            if (peer.SentReliableCommands.Empty)
            {
                return commandNumber;
            }

            outgoingCommand = peer.SentReliableCommands.Begin.Value;

            peer.NextTimeout = outgoingCommand.SentTime + outgoingCommand.RoundTripTimeout;

            return commandNumber;
        }

        private int HandleConnect(ENetAddress receivedAddress, ref ENetPeer result, ENetProtocol.Connect command)
        {
            uint channelCount = command.ChannelCount;

            if (channelCount < MINIMUM_CHANNEL_COUNT || channelCount > MAXIMUM_CHANNEL_COUNT)
            {
                return -1;
            }

            foreach (var peer in Peers)
            {
                if (peer.State != ENetPeerState.DISCONNECTED
                    && peer.Address.Host == receivedAddress.Host
                    && peer.Address.Port == receivedAddress.Port
                    && peer.SessionID == command.SessionID)
                {
                    return -1;
                }
            }

            ENetPeer currentPeer = Peers.Find((peer) => peer.State == ENetPeerState.DISCONNECTED);

            if (currentPeer == null)
            {
                return -1;
            }

            if (channelCount > ChannelLimit)
            {
                channelCount = ChannelLimit;
            }

            currentPeer.Channels = Utils.MakeList<ENetChannel>(channelCount);
            currentPeer.State = ENetPeerState.ACKNOWLEDGING_CONNECT;
            currentPeer.SessionID = command.SessionID;
            currentPeer.Address = receivedAddress;
            currentPeer.OutgoingPeerID = command.OutgoingPeerID;
            currentPeer.IncomingBandwidth = command.IncomingBandwidth;
            currentPeer.OutgoingBandwidth = command.OutgoingBandwidth;
            currentPeer.PacketThrottleInterval = command.PacketThrottleInterval;
            currentPeer.PacketThrottleAcceleration = command.PacketThrottleAcceleration;
            currentPeer.PacketThrottleDeceleration = command.PacketThrottleDeceleration;
            currentPeer.MTU = Math.Clamp(command.MTU, MINIMUM_MTU, MAXIMUM_MTU);

            if (OutgoingBandwidth == 0
                && currentPeer.IncomingBandwidth == 0)
            {
                currentPeer.WindowSize = MAXIMUM_WINDOW_SIZE;
            }
            else if (OutgoingBandwidth == 0 
                || currentPeer.IncomingBandwidth == 0)
            {
                currentPeer.WindowSize = (Math.Max(OutgoingBandwidth, currentPeer.IncomingBandwidth) / ENetPeer.WINDOW_SIZE_SCALE) * MINIMUM_WINDOW_SIZE;
            }
            else
            {
                currentPeer.WindowSize = (Math.Min(OutgoingBandwidth, currentPeer.IncomingBandwidth) / ENetPeer.WINDOW_SIZE_SCALE) * MINIMUM_WINDOW_SIZE;
            }

            currentPeer.WindowSize = Math.Clamp(currentPeer.WindowSize, MINIMUM_WINDOW_SIZE, MAXIMUM_WINDOW_SIZE);

            uint windowSize = IncomingBandwidth == 0 ? MAXIMUM_WINDOW_SIZE : (IncomingBandwidth / ENetPeer.WINDOW_SIZE_SCALE) * MINIMUM_WINDOW_SIZE;

            if (windowSize > command.WindowSize)
            {
                windowSize = command.WindowSize;
            }

            windowSize = Math.Clamp(windowSize, MINIMUM_WINDOW_SIZE, MAXIMUM_WINDOW_SIZE);

            var verifyCommand = new ENetProtocol.VerifyConnect
            {
                Flags = ENetCommandFlag.ACKNOWLEDGE,
                ChannelID = 0xFF,
                OutgoingPeerID = currentPeer.IncomingPeerID,
                MTU = currentPeer.MTU,
                WindowSize = windowSize,
                ChannelCount = channelCount,
                IncomingBandwidth = IncomingBandwidth,
                OutgoingBandwidth = OutgoingBandwidth,
                PacketThrottleInterval = currentPeer.PacketThrottleInterval,
                PacketThrottleAcceleration = currentPeer.PacketThrottleAcceleration,
                PacketThrottleDeceleration = currentPeer.PacketThrottleDeceleration,
            };

            currentPeer.QueueOutgoingCommand(verifyCommand, null, 0, 0);

            result = currentPeer;
            return 0;
        }

        private int HandleSendReliable(ENetPeer peer, ENetProtocol.Send.Reliable command, ENetBuffer buffer)
        {
            if (command.ChannelID >= peer.ChannelCount)
            {
                return -1;
            }

            if (peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
            {
                return -1;
            }

            if (command.DataLength > buffer.BytesLeft)
            {
                return -1;
            }

            var packet = new ENetPacket
            {
                Data = buffer.ReadBytes(command.DataLength),
                Flags = ENetPacketFlags.Reliable
            };

            if (peer.QueueIncomingCommand(command, packet, 0) == null)
            {
                return -1;
            }

            return 0;
        }

        private int HandleSendUnsequenced(ENetPeer peer, ENetProtocol.Send.Unsequenced command, ENetBuffer buffer)
        {

            if (command.ChannelID >= peer.ChannelCount)
            {
                return -1;
            }

            if (peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
            {
                return -1;
            }

            if (command.DataLength > buffer.BytesLeft)
            {
                return -1;
            }

            uint unsequencedGroup = command.UnsequencedGroup;
            uint index = unsequencedGroup % ENetPeer.UNSEQUENCED_WINDOW_SIZE;

            if (unsequencedGroup < peer.IncomingUnsequencedGroup)
            {
                unsequencedGroup += 0x10000u;
            }

            if (unsequencedGroup >= (uint)peer.IncomingUnsequencedGroup + ENetPeer.FREE_UNSEQUENCED_WINDOWS * ENetPeer.UNSEQUENCED_WINDOW_SIZE)
            {
                return 0;
            }

            unsequencedGroup &= 0xFFFFu;

            if (unsequencedGroup - index != peer.IncomingUnsequencedGroup)
            {
                peer.IncomingUnsequencedGroup = (ushort)(unsequencedGroup - index);
                
                peer.UnsequencedWindow.SetAll(false);
            }
            else if (peer.UnsequencedWindow[(int)index])
            {
                return 0;
            }

            var packet = new ENetPacket
            {
                Data = buffer.ReadBytes(command.DataLength),
                Flags = ENetPacketFlags.Unsequenced
            };

            if (peer.QueueIncomingCommand(command, packet, 0) == null)
            {
                return -1;
            }

            peer.UnsequencedWindow[(int)index] = true;

            return 0;
        }

        private int HandleSendUnreliable(ENetPeer peer, ENetProtocol.Send.Unreliable command, ENetBuffer buffer)
        {
            if (command.ChannelID >= peer.ChannelCount)
            {
                return -1;
            }

            if (peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
            {
                return -1;
            }

            if (command.DataLength > buffer.BytesLeft)
            {
                return -1;
            }

            var packet = new ENetPacket
            {
                Data = buffer.ReadBytes(command.DataLength)
            };

            if (peer.QueueIncomingCommand(command, packet, 0) == null)
            {
                return -1;
            }

            return 0;
        }

        private int HandleSendFragment(ENetPeer peer, ENetProtocol.Send.Fragment command, ENetBuffer buffer)
        {
            if (command.ChannelID >= peer.ChannelCount)
            {
                return -1;
            }

            if (peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
            {
                return -1;
            }

            if (command.DataLength > buffer.BytesLeft)
            {
                return -1;
            }

            uint fragmentLength = command.DataLength;
            var channel = peer.Channels[command.ChannelID];
            uint startSequenceNumber = command.StartSequenceNumber;
            ushort startWindow = (ushort)(startSequenceNumber / ENetPeer.RELIABLE_WINDOW_SIZE);
            ushort currentWindow = (ushort)(channel.IncomingReliableSequenceNumber / ENetPeer.RELIABLE_WINDOW_SIZE);

            if (startSequenceNumber < channel.IncomingReliableSequenceNumber)
            {
                startWindow += ENetPeer.RELIABLE_WINDOWS;
            }

            if (startWindow < currentWindow || startWindow >= currentWindow + ENetPeer.FREE_RELIABLE_WINDOWS - 1)
            {
                return 0;
            }

            uint fragmentNumber = command.FragmentNumber;
            uint fragmentCount = command.FragmentCount;
            uint fragmentOffset = command.FragmentOffset;
            uint totalLength = command.TotalLength;

            if (fragmentOffset >= totalLength
                || fragmentOffset + fragmentLength > totalLength
                || fragmentNumber >= fragmentCount)
            {
                return -1;
            }


            ENetIncomingCommand startCommand = null;
            for (var currentCommand = channel.IncomingReliableCommands.End.Prev;
                 currentCommand != channel.IncomingReliableCommands.End;
                 currentCommand = currentCommand.Prev)
            {
                var incomingCommand = currentCommand.Value;

                if (startSequenceNumber >= channel.IncomingReliableSequenceNumber)
                {
                    if (incomingCommand.ReliableSequenceNumber < channel.IncomingReliableSequenceNumber)
                    {
                        continue;
                    }
                }
                else if (incomingCommand.ReliableSequenceNumber >= channel.IncomingReliableSequenceNumber)
                {
                    break;
                }

                if (incomingCommand.ReliableSequenceNumber <= startSequenceNumber)
                {
                    if (incomingCommand.ReliableSequenceNumber < startSequenceNumber)
                    {
                        break;
                    }

                    if (!(incomingCommand.Command is ENetProtocol.Send.Fragment)
                        || totalLength != incomingCommand.Packet.DataLength
                        || fragmentCount != incomingCommand.FragmentCount)
                    {

                        return -1;
                    }

                    startCommand = incomingCommand;
                    break;
                }
            }

            if (startCommand == null)
            {
                var packet = new ENetPacket
                {
                    Data = new byte[totalLength],
                    Flags = ENetPacketFlags.Reliable,
                };

                var hostCommand = command;

                hostCommand.ReliableSequenceNumber = (ushort)startSequenceNumber;
                hostCommand.StartSequenceNumber = (ushort)startSequenceNumber;
                hostCommand.DataLength = (ushort)fragmentLength;
                hostCommand.FragmentNumber = fragmentNumber;
                hostCommand.FragmentCount = fragmentCount;
                hostCommand.FragmentOffset = fragmentOffset;
                hostCommand.TotalLength = totalLength;

                startCommand = peer.QueueIncomingCommand(hostCommand, packet, fragmentCount);
            }

            if (!startCommand.Fragments[(int)fragmentNumber])
            {
                startCommand.FragmentsRemaining--;

                startCommand.Fragments[(int)fragmentNumber] = true;

                if (fragmentOffset + fragmentLength > startCommand.Packet.DataLength)
                {
                    fragmentLength = startCommand.Packet.DataLength - fragmentOffset;
                }

                buffer.ReadBytes(startCommand.Packet.Data, fragmentOffset, fragmentLength);

                if (startCommand.FragmentsRemaining <= 0)
                {
                    peer.DispatchIncomingReliableCommands(channel);
                }
            }

            return 0;
        }

        private int HandleBandwidthLimit(ENetPeer peer, ENetProtocol.BandwidthLimit command)
        {
            peer.IncomingBandwidth = command.IncomingBandwidth;
            peer.OutgoingBandwidth = command.OutgoingBandwidth;

            if (peer.IncomingBandwidth == 0u && OutgoingBandwidth == 0u)
            {
                peer.WindowSize = MAXIMUM_WINDOW_SIZE;
            }
            else
            {
                peer.WindowSize = (Math.Min(peer.IncomingBandwidth, OutgoingBandwidth) / ENetPeer.WINDOW_SIZE_SCALE) * MINIMUM_WINDOW_SIZE;
            }

            peer.WindowSize = Math.Clamp(peer.WindowSize, MINIMUM_WINDOW_SIZE, MAXIMUM_WINDOW_SIZE);
            
            return 0;
        }

        private int HandleThrottleConfigure(ENetPeer peer, ENetProtocol.ThrottleConfigure command)
        {
            peer.PacketThrottleInterval = command.PacketThrottleInterval;
            peer.PacketThrottleAcceleration = command.PacketThrottleAcceleration;
            peer.PacketThrottleDeceleration = command.PacketThrottleDeceleration;
            
            return 0;
        }

        private int HandleDisconnect(ENetPeer peer, ENetProtocol.Disconnect command)
        {
            if (peer.State == ENetPeerState.ZOMBIE || peer.State == ENetPeerState.ACKNOWLEDGING_DISCONNECT)
            {
                return 0;
            }

            peer.ResetQueues();

            if (peer.State == ENetPeerState.CONNECTION_SUCCEEDED || peer.State == ENetPeerState.DISCONNECTING)
            {
                DispatchState(peer, ENetPeerState.ZOMBIE);
            }
            else if (peer.State != ENetPeerState.CONNECTED && peer.State != ENetPeerState.DISCONNECT_LATER)
            {
                if (peer.State == ENetPeerState.CONNECTION_PENDING)
                {
                    RecalculateBandwidthLimits = true;
                }

                peer.Reset();
            }
            else if (command.Flags.HasFlag(ENetCommandFlag.ACKNOWLEDGE))
            {
                peer.State = ENetPeerState.ACKNOWLEDGING_DISCONNECT;
            }
            else
            {
                DispatchState(peer, ENetPeerState.ZOMBIE);
            }

            peer.DisconnectData = command.Data;
            return 0;
        }

        private int HandleAcknowledge(ENetEvent evnt, ENetPeer peer, ENetProtocol.Acknowledge command)
        {
            uint receivedSentTime = command.ReceivedSentTime;
            receivedSentTime |= ServiceTime & 0xFFFF0000u;

            if ((receivedSentTime & 0x8000u) > (ServiceTime & 0x8000u))
            {
                receivedSentTime -= 0x10000u;
            }

            if (ENET_TIME_LESS(ServiceTime, receivedSentTime))
            {
                return 0;
            }

            peer.LastReceiveTime = ServiceTime;
            peer.EarliestTimeout = 0;

            uint roundTripTime = ENET_TIME_DIFFERENCE(ServiceTime, receivedSentTime);

            peer.Throttle(roundTripTime);

            peer.RoundTripTimeVariance -= peer.RoundTripTimeVariance / 4u;

            if (roundTripTime >= peer.RoundTripTime)
            {
                peer.RoundTripTime += (roundTripTime - peer.RoundTripTime) / 8u;
                peer.RoundTripTimeVariance += (roundTripTime - peer.RoundTripTime) / 4u;
            }
            else
            {
                peer.RoundTripTime -= (peer.RoundTripTime - roundTripTime) / 8u;
                peer.RoundTripTimeVariance += (peer.RoundTripTime - roundTripTime) / 4u;
            }

            if (peer.RoundTripTime < peer.LowestRoundTripTime)
            {
                peer.LowestRoundTripTime = peer.RoundTripTime;
            }

            if (peer.RoundTripTimeVariance > peer.HighestRoundTripTimeVariance)
            {
                peer.HighestRoundTripTimeVariance = peer.RoundTripTimeVariance;
            }

            if (peer.PacketThrottleEpoch == 0
                || ENET_TIME_DIFFERENCE(ServiceTime, peer.PacketThrottleEpoch) >= peer.PacketThrottleInterval)
            {
                peer.LastRoundTripTime = peer.LowestRoundTripTime;
                peer.LastRoundTripTimeVariance = peer.HighestRoundTripTimeVariance;
                peer.LowestRoundTripTime = peer.RoundTripTime;
                peer.HighestRoundTripTimeVariance = peer.RoundTripTimeVariance;
                peer.PacketThrottleEpoch = ServiceTime;
            }

            uint receivedReliableSequenceNumber = command.ReceivedReliableSequenceNumber;

            var commandNumber = RemoveSentReliableCommand(peer, (ushort)receivedReliableSequenceNumber, command.ChannelID);

            switch (peer.State)
            {
                case ENetPeerState.ACKNOWLEDGING_CONNECT:
                    if (commandNumber != ENetProtocolCommand.VERIFY_CONNECT)
                    {
                        return -1;
                    }

                    NotifyConnect(peer, evnt);
                    break;

                case ENetPeerState.DISCONNECTING:
                    if (commandNumber != ENetProtocolCommand.DISCONNECT)
                    {
                        return -1;
                    }

                    NotifyDisconnect(peer, evnt);
                    break;

                case ENetPeerState.DISCONNECT_LATER:
                    if (peer.OutgoingReliableCommands.Empty 
                        && peer.OutgoingUnreliableCommands.Empty
                        && peer.SentReliableCommands.Empty)
                    {
                        peer.Disconnect(peer.DisconnectData);
                    }
                    break;

                default:
                    break;
            }

            return 0;
        }

        private int HandleVerifyConnect(ENetEvent evnt, ENetPeer peer, ENetProtocol.VerifyConnect command)
        {
            if (peer.State != ENetPeerState.CONNECTING)
            {
                return 0;
            }

            uint channelCount = command.ChannelCount;
            
            if (channelCount < MINIMUM_CHANNEL_COUNT || channelCount > MAXIMUM_CHANNEL_COUNT
                || command.PacketThrottleInterval != peer.PacketThrottleInterval
                || command.PacketThrottleAcceleration != peer.PacketThrottleAcceleration
                || command.PacketThrottleDeceleration != peer.PacketThrottleDeceleration)
            {
                DispatchState(peer, ENetPeerState.ZOMBIE);
                return -1;
            }

            RemoveSentReliableCommand(peer, 1, 0xFF);

            if (channelCount < peer.ChannelCount)
            {
                peer.Channels.RemoveRange((int)channelCount, (int)(peer.ChannelCount - channelCount));
            }

            peer.OutgoingPeerID = command.OutgoingPeerID;

            ushort mtu = Math.Clamp(command.MTU, MINIMUM_MTU, MAXIMUM_MTU);
            
            if (mtu < peer.MTU)
            {
                peer.MTU = mtu;
            }

            uint windowSize = Math.Clamp(command.WindowSize, MINIMUM_WINDOW_SIZE, MAXIMUM_WINDOW_SIZE);
            
            if (windowSize < peer.WindowSize)
            {
                peer.WindowSize = windowSize;
            }

            peer.IncomingBandwidth = command.IncomingBandwidth;
            peer.OutgoingBandwidth = command.OutgoingBandwidth;

            NotifyConnect(peer, evnt);
            
            return 0;
        }

        private int HandleIncomingCommands(ENetEvent evnt, ENetAddress receivedAddress, ENetBuffer buffer)
        {
            uint receivedDataLength = buffer.DataLength;

            var header = ENetProtocolHeader.Create(buffer, Version);

            if (header == null)
            {
                return 0;
            }

            ushort peerID = header.PeerID;
            ENetPeer peer = null;

            if (peerID != Version.MaxPeerID)
            {
                if (peerID > PeerCount)
                {
                    return 0;
                }

                peer = Peers[peerID];

                if (peer.State == ENetPeerState.DISCONNECTED 
                    || peer.State == ENetPeerState.ZOMBIE)
                {
                    return 0;
                }

                if ((receivedAddress.Host != peer.Address.Host
                    || receivedAddress.Port != peer.Address.Port)
                        && peer.Address.Host != ENetAddress.Broadcast)
                {
                    return 0;
                }

                if (header.SessionID != peer.SessionID)
                {
                    return 0;
                }

                peer.Address = receivedAddress;
                peer.IncomingDataTotal += receivedDataLength;
            }

            while (buffer.BytesLeft > 0)
            {
                var command = ENetProtocol.Create(buffer, Version);

                if (command == null || command is ENetProtocol.None)
                {
                    break;
                }

                if (peer == null && !(command is ENetProtocol.Connect))
                {
                    break;
                }

                int result = command switch
                {
                    ENetProtocol.Acknowledge c => HandleAcknowledge(evnt, peer, c),
                    ENetProtocol.Connect c => HandleConnect(receivedAddress, ref peer, c),
                    ENetProtocol.VerifyConnect c => HandleVerifyConnect(evnt, peer, c),
                    ENetProtocol.Disconnect c => HandleDisconnect(peer, c),
                    ENetProtocol.Ping _ => 0,
                    ENetProtocol.Send.Reliable c => HandleSendReliable(peer, c, buffer),
                    ENetProtocol.Send.Unreliable c => HandleSendUnreliable(peer, c, buffer),
                    ENetProtocol.Send.Unsequenced c => HandleSendUnsequenced(peer, c, buffer),
                    ENetProtocol.Send.Fragment c => HandleSendFragment(peer, c, buffer),
                    ENetProtocol.BandwidthLimit c => HandleBandwidthLimit(peer, c),
                    ENetProtocol.ThrottleConfigure c => HandleThrottleConfigure(peer, c),
                    _ => -1,
                };

                if (result != 0)
                {
                    break;
                }

                if (peer != null && command.Flags.HasFlag(ENetCommandFlag.ACKNOWLEDGE))
                {
                    if (header.TimeSent is ushort sentTime)
                    {
                        switch (peer.State)
                        {
                            case ENetPeerState.DISCONNECTING:
                            case ENetPeerState.ACKNOWLEDGING_CONNECT:
                                break;

                            case ENetPeerState.ACKNOWLEDGING_DISCONNECT:
                                if (command is ENetProtocol.Disconnect)
                                {
                                    peer.QueueAcknowledgement(command, sentTime);
                                }
                                break;

                            default:
                                peer.QueueAcknowledgement(command, sentTime);
                                break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (evnt != null && evnt.Type != ENetEventType.NONE)
            {
                return 1;
            }
            return 0;
        }

        private int ReceiveIncomingCommands(ENetEvent evnt)
        {
            var buffer = new ENetBuffer(MAXIMUM_MTU);
            for (; ; )
            {
                var receivedAddress = new ENetAddress(ENetAddress.Any, 0);
                var length = Socket.ReceiveFrom(ref receivedAddress, buffer);

                if (length < 0)
                {
                    return -1;
                }

                if (length == 0)
                {
                    return 0;
                }

                buffer.DataLength = (uint)length;
                TotalReceivedData += (uint)length;
                TotalReceivedPackets++;

                switch (HandleIncomingCommands(evnt, receivedAddress, buffer))
                {
                    case 1:
                        return 1;
                    case -1:
                        return -1;
                    default:
                        break;
                }
            }
        }

        private void SendAcknowledgements(ENetPeer peer, ENetBuffer buffer, ref bool continueSending) 
        {
            var currentAcknowledgement = peer.Acknowledgements.Begin;
            while (currentAcknowledgement != peer.Acknowledgements.End)
            {
                if (ENetProtocol.Acknowledge.SIZE > buffer.BytesLeft)
                {
                    continueSending = true;

                    break;
                }

                var acknowledgement = currentAcknowledgement.Value;
                
                currentAcknowledgement = currentAcknowledgement.Next;

                var command = new ENetProtocol.Acknowledge
                {
                    ChannelID = acknowledgement.command.ChannelID,
                    ReceivedReliableSequenceNumber = acknowledgement.command.ReliableSequenceNumber,
                    ReceivedSentTime = (ushort)acknowledgement.SentTime,
                };

                command.Write(buffer, Version);

                if (acknowledgement.command is ENetProtocol.Disconnect)
                {
                    DispatchState(peer, ENetPeerState.ZOMBIE);
                }

                acknowledgement.Node.Remove();
            }
        }

        private void SendUnreliableOutgoingCommands(ENetPeer peer, ENetBuffer buffer, ref bool continueSending) 
        {
            var currentCommand = peer.OutgoingUnreliableCommands.Begin;
            
            while (currentCommand != peer.OutgoingUnreliableCommands.End)
            {
                var outgoingCommand = currentCommand.Value;
                uint commandSize = outgoingCommand.Command.Size;

                if(outgoingCommand.Packet != null)
                {
                    commandSize += outgoingCommand.Packet.DataLength;
                }

                if(commandSize > buffer.BytesLeft)
                {
                    continueSending = true;

                    break;
                }

                currentCommand = currentCommand.Next;

                if(outgoingCommand.Packet != null)
                {
                    peer.PacketThrottleCounter += ENetPeer.PACKET_THROTTLE_COUNTER;
                    peer.PacketThrottleCounter %= ENetPeer.PACKET_THROTTLE_SCALE;
                   
                    if(peer.PacketThrottleCounter > peer.PacketThrottle)
                    {
                        outgoingCommand.Node.Remove();
                        continue;
                    }
                }

                outgoingCommand.Command.Write(buffer, Version);
                outgoingCommand.Node.Remove();

                if(outgoingCommand.Packet != null)
                {
                    buffer.WriteBytes(outgoingCommand.Packet.Data);

                    peer.SentUnreliableCommands.End.Insert(outgoingCommand.Node);
                }
            }

            if (peer.State == ENetPeerState.DISCONNECT_LATER
                && peer.OutgoingReliableCommands.Empty
                && peer.OutgoingUnreliableCommands.Empty
                && peer.SentReliableCommands.Empty)
            {
                peer.Disconnect(peer.DisconnectData);
            }
        }

        private int CheckTimeouts(ENetPeer peer, ENetEvent evnt)
        {
            var currentCommand = peer.SentReliableCommands.Begin;
            var insertPosition = peer.OutgoingReliableCommands.Begin;

            while (currentCommand != peer.SentReliableCommands.End)
            {
                var outgoingCommand = currentCommand.Value;

                currentCommand = currentCommand.Next;

                if (ENET_TIME_DIFFERENCE(ServiceTime, outgoingCommand.SentTime) < outgoingCommand.RoundTripTimeout)
                {
                    continue;
                }

                if (peer.EarliestTimeout == 0
                    || ENET_TIME_LESS(outgoingCommand.SentTime, peer.EarliestTimeout))
                {
                    peer.EarliestTimeout = outgoingCommand.SentTime;
                }

                if (peer.EarliestTimeout != 0
                    && (ENET_TIME_DIFFERENCE(ServiceTime, peer.EarliestTimeout) >= ENetPeer.TIMEOUT_MAXIMUM
                        || (outgoingCommand.RoundTripTimeout >= outgoingCommand.RoundTripTimeoutLimit
                            && ENET_TIME_DIFFERENCE(ServiceTime, peer.EarliestTimeout) >= ENetPeer.TIMEOUT_MINIMUM)))
                {
                    NotifyDisconnect(peer, evnt);

                    return 1;
                }

                if (outgoingCommand.Packet != null)
                {
                    peer.ReliableDataInTransit -= outgoingCommand.FragmentLength;
                }

                peer.PacketsLost++;

                outgoingCommand.RoundTripTimeout *= 2;

                insertPosition.Insert(outgoingCommand.Node.Remove());

                // CHECKME: what does this do exactly? checks if we removed first command ???
                if (currentCommand == peer.SentReliableCommands.Begin
                    && !peer.SentReliableCommands.Empty)
                {
                    outgoingCommand = currentCommand.Value;

                    peer.NextTimeout = outgoingCommand.SentTime + outgoingCommand.RoundTripTimeout;
                }
            }

            return 0;
        }

        private void SendReliableOutgoingCommands(ENetPeer peer, ENetBuffer buffer, ref bool continueSending, ref bool hasSentTime)
        {
            var currentCommand = peer.OutgoingReliableCommands.Begin;
            
            while(currentCommand != peer.OutgoingReliableCommands.End)
            {
                var outgoingCommand = currentCommand.Value;

                var channel = outgoingCommand.Command.ChannelID < peer.ChannelCount ? peer.Channels[outgoingCommand.Command.ChannelID] : null;
                ushort reliableWindow = (ushort)(outgoingCommand.ReliableSequenceNumber / ENetPeer.RELIABLE_WINDOW_SIZE);

                if(channel !=null 
                    && outgoingCommand.SendAttempts < 1
                    && (outgoingCommand.ReliableSequenceNumber % ENetPeer.RELIABLE_WINDOW_SIZE) == 0)
                {
                    if (channel.ReliableWindows[(reliableWindow + ENetPeer.RELIABLE_WINDOWS - 1) % ENetPeer.RELIABLE_WINDOWS] >= ENetPeer.RELIABLE_WINDOW_SIZE
                        || (channel.UsedReliableWindows & ((ENetPeer.FREE_RELIABLE_WINDOWS_MASK << reliableWindow) 
                            | (ENetPeer.FREE_RELIABLE_WINDOWS_MASK >> (ENetPeer.RELIABLE_WINDOW_SIZE - reliableWindow)))) != 0u)
                    {
                        break;
                    }
                }

                uint commandSize = outgoingCommand.Command.Size;

                if (commandSize > buffer.BytesLeft)
                {
                    continueSending = true;
                    break;
                }

                if(outgoingCommand.Packet != null)
                {
                    uint windowSize = (peer.PacketThrottle * peer.WindowSize) / ENetPeer.PACKET_THROTTLE_SCALE;

                    if(peer.ReliableDataInTransit + outgoingCommand.FragmentLength > Math.Max(windowSize, peer.MTU))
                    {
                        break;
                    }

                    if((ushort)(commandSize + outgoingCommand.FragmentLength) > (ushort)buffer.BytesLeft)
                    {
                        continueSending = true;

                        break;
                    }
                }

                currentCommand = currentCommand.Next;

                if(channel != null && outgoingCommand.SendAttempts < 1)
                {
                    channel.UsedReliableWindows |= (ushort)(1u << reliableWindow);
                    channel.ReliableWindows[reliableWindow]++;
                }

                outgoingCommand.SendAttempts++;

                if(outgoingCommand.RoundTripTimeout == 0)
                {
                    outgoingCommand.RoundTripTimeout = peer.RoundTripTime + 4u * peer.RoundTripTimeVariance;
                    outgoingCommand.RoundTripTimeoutLimit = ENetPeer.TIMEOUT_LIMIT * outgoingCommand.RoundTripTimeout;
                }

                if(peer.SentReliableCommands.Empty)
                {
                    peer.NextTimeout = ServiceTime + outgoingCommand.RoundTripTimeout;
                }

                peer.SentReliableCommands.End.Insert(outgoingCommand.Node.Remove());
                
                outgoingCommand.SentTime = ServiceTime;

                var command = outgoingCommand.Command;
                hasSentTime = true;
                command.Write(buffer, Version);

                if(outgoingCommand.Packet != null)
                {
                    buffer.WriteBytes(outgoingCommand.Packet.Data, outgoingCommand.FragmentOffset, outgoingCommand.FragmentLength);
                    
                    peer.ReliableDataInTransit += outgoingCommand.FragmentLength;
                }

                peer.PacketsSent++;
            }
        }

        private int SendOutgoingCommands(ENetEvent evnt, bool checkForTimeout)
        {

            var buffer = new ENetBuffer(MAXIMUM_MTU);

            bool continueSending = true;


            while (continueSending)
            {
                continueSending = false;

                foreach (var currentPeer in Peers)
                {
                    if (currentPeer.State == ENetPeerState.DISCONNECTED || currentPeer.State == ENetPeerState.ZOMBIE)
                    {
                        continue;
                    }

                    bool hasSentTime = false;
                    buffer.Position = Version.MaxHeaderSizeSend;
                    buffer.DataLength = currentPeer.MTU;

                    if (!currentPeer.Acknowledgements.Empty)
                    {
                        SendAcknowledgements(currentPeer, buffer, ref continueSending);
                    }

                    if (checkForTimeout
                        && !currentPeer.SentReliableCommands.Empty
                        && !ENET_TIME_LESS(ServiceTime, currentPeer.NextTimeout))
                    {
                        if (CheckTimeouts(currentPeer, evnt) == 1)
                        {
                            return 1;
                        }
                    }

                    if (!currentPeer.OutgoingReliableCommands.Empty)
                    {
                        SendReliableOutgoingCommands(currentPeer, buffer, ref continueSending, ref hasSentTime);
                    }
                    else if (currentPeer.SentReliableCommands.Empty
                        && ENET_TIME_DIFFERENCE(ServiceTime, currentPeer.LastReceiveTime) >= ENetPeer.PING_INTERVAL)
                    {
                        if (ENetProtocol.Ping.SIZE <= buffer.BytesLeft)
                        {
                            currentPeer.Ping();
                            SendReliableOutgoingCommands(currentPeer, buffer, ref continueSending, ref hasSentTime);
                        }
                    }

                    if (!currentPeer.OutgoingUnreliableCommands.Empty)
                    {
                        SendUnreliableOutgoingCommands(currentPeer, buffer, ref continueSending);
                    }

                    if (buffer.Position <= Version.MaxHeaderSizeSend)
                    {
                        continue;
                    }

                    if (currentPeer.PacketLossEpoch == 0u)
                    {
                        currentPeer.PacketLossEpoch = ServiceTime;
                    }
                    else if (ENET_TIME_DIFFERENCE(ServiceTime, currentPeer.PacketLossEpoch) >= ENetPeer.PACKET_LOSS_INTERVAL
                        && currentPeer.PacketsSent > 0u)
                    {
                        uint packetLoss = currentPeer.PacketsLost * ENetPeer.PACKET_LOSS_SCALE / currentPeer.PacketsSent;

                        currentPeer.PacketLossVariance -= currentPeer.PacketLossVariance / 4u;

                        if (packetLoss >= currentPeer.PacketLoss)
                        {
                            currentPeer.PacketLoss += (packetLoss - currentPeer.PacketLoss) / 8u;
                            currentPeer.PacketLossVariance += (packetLoss - currentPeer.PacketLoss) / 4u;
                        }
                        else
                        {
                            currentPeer.PacketLoss -= (currentPeer.PacketLoss - packetLoss) / 8u;
                            currentPeer.PacketLossVariance += (currentPeer.PacketLoss - packetLoss) / 4u;
                        }

                        currentPeer.PacketLossEpoch = ServiceTime;
                        currentPeer.PacketsSent = 0;
                        currentPeer.PacketsLost = 0;
                    }


                    uint bufferLength = buffer.Position;
                    uint bufferOffset = 0;

                    var header = new ENetProtocolHeader
                    {
                        SessionID = currentPeer.SessionID,
                        PeerID = currentPeer.OutgoingPeerID,
                    };
                    if (hasSentTime)
                    {
                        header.TimeSent = (ushort)ServiceTime;
                    }
                    else
                    {
                        header.TimeSent = null;
                        bufferOffset += 2;
                        bufferLength -= 2;
                    }
                    buffer.Position = bufferOffset;
                    header.Write(buffer, Version);

                    currentPeer.LastSendTime = ServiceTime;

                    int sentLength = Socket.SendTo(currentPeer.Address, buffer.Data, bufferOffset, bufferLength);

                    RemoveSentUnreliableCommands(currentPeer);

                    if (sentLength < 0)
                    {
                        return -1;
                    }

                    TotalSentData += (uint)sentLength;
                    TotalSentPackets++;
                }
            }

            return 0;
        }

        /* Public API */

        public void Flush()
        {
            ServiceTime = GetTime();

            SendOutgoingCommands(null, false);
        }

        public int CheckEvents(ENetEvent evnt)
        {
            if (evnt != null)
            {
                evnt.Type = ENetEventType.NONE;
                evnt.Peer = null;
                evnt.Packet = null;
                return DispatchIncomingCommands(evnt);
            }
            else
            {
                return -1;
            }
        }

        public int HostService(ENetEvent evnt, uint timeout)
        {
            var waitCondition = new List<Socket> { };

            if (evnt != null)
            {
                evnt.Type = ENetEventType.NONE;
                evnt.Peer = null;
                evnt.Packet = null;

                switch (DispatchIncomingCommands(evnt))
                {
                    case 1:
                        return 1;
                    case -1:
                        return -1;
                    default:
                        break;
                }
            }

            ServiceTime = GetTime();

            timeout += ServiceTime;

            do
            {
                if (ENET_TIME_DIFFERENCE(ServiceTime, BandwidthThrottleEpoch) >= BANDWIDTH_THROTTLE_INTERVAL)
                {
                    BandwidthThrottle();
                }

                switch (SendOutgoingCommands(evnt, true))
                {
                    case 1:
                        return 1;
                    case -1:
                        return -1;
                    default:
                        break;
                }

                switch (ReceiveIncomingCommands(evnt))
                {
                    case 1:
                        return 1;
                    case -1:
                        return -1;
                    default:
                        break;
                }

                switch (SendOutgoingCommands(evnt, true))
                {
                    case 1:
                        return 1;
                    case -1:
                        return -1;
                    default:
                        break;
                }

                if (evnt != null)
                {
                    switch (DispatchIncomingCommands(evnt))
                    {
                        case 1:
                            return 1;
                        case -1:
                            return -1;
                        default:
                            break;
                    }
                }

                ServiceTime = GetTime();

                if (!ENET_TIME_LESS(ServiceTime, timeout))
                {
                    return 0;
                }

                waitCondition.Clear();
                waitCondition.Add(Socket);

                try
                {
                    var time = ENET_TIME_DIFFERENCE(timeout, ServiceTime);
                    Socket.Select(waitCondition, null, null, (int)time);
                }
                catch (SocketException)
                {
                    return -1;
                }

                ServiceTime = GetTime();

            } while (waitCondition.Count > 0);

            return 0;
        }
    }
}
