using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;

namespace LENet
{
    internal static class Utils
    {
        public static List<T> MakeList<T>(uint count) where T : new()
        {
            var result = new List<T>((int)count);
            for(var i = 0; i < count; i++)
            {
                result.Add(new T());
            }
            return result;
        }

        public static T[] MakeArray<T>(uint count) where T : new()
        {
            var result = new T[(int)count];
            for (var i = 0; i < count; i++)
            {
                result[i] = new T();
            }
            return result;
        }

        public static int ReceiveFrom(this Socket socket, ref ENetAddress receivedAddres, ENetBuffer buffer)
        {
            int receivedLength;
            var endPoint = new IPEndPoint(receivedAddres.Host, receivedAddres.Port) as EndPoint;

            try
            {
                receivedLength = socket.ReceiveFrom(buffer.Data, ref endPoint);
            }
            catch(SocketException error)
            {
                if (error.SocketErrorCode != SocketError.WouldBlock && error.SocketErrorCode != SocketError.ConnectionReset)
                {
                    return -1;
                }

                return 0;
            }
            
            if(receivedLength == 0)
            {
                return 0;
            }

            buffer.Position = 0;
            buffer.DataLength = (uint)receivedLength;
            receivedAddres = new ENetAddress(endPoint as IPEndPoint);
            return receivedLength;
        }

        public static int SendTo(this Socket socket, ENetAddress address, byte[] data, uint offset, uint length)
        {
            int sentLength;
            var endpoint = new IPEndPoint(address.Host, address.Port);

            try
            {
                sentLength = socket.SendTo(data, (int)offset, (int)(length), SocketFlags.None, endpoint);
            }
            catch(SocketException error)
            {
                if(error.SocketErrorCode == SocketError.WouldBlock)
                {
                    return 0;
                }
                return -1;
            }

            return sentLength;
        }
    }
}
