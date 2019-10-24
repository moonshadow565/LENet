using LENet;
using System;

namespace DummyServer
{
    class Program
    {

        public class Server
        {
            public ENetHost server;
            public Server(ENetAddress address)
            {
                server = ENetHost.Create(address, 32, 0, 0);
            }

            public void RunOnce()
            {
                int result;
                do
                {
                    var evnt = new ENetEvent();
                    result = server.HostService(evnt, 100);
                    switch (evnt.Type)
                    {
                        case ENetEventType.CONNECT:
                            Console.WriteLine($"[Server] Peer ({evnt.Peer.SessionID}) connected!");
                            evnt.Peer.Send(0, new ENetPacket
                            {
                                Flags = ENetPacketFlags.Reliable,
                                Data = new byte[9001]
                            });
                            break;
                        case ENetEventType.DISCONNECT:
                            Console.WriteLine($"[Server] Peer ({evnt.Peer.SessionID}) diconnected!");
                            break;
                        case ENetEventType.RECEIVE:
                            Console.WriteLine($"[Server] Peer ({evnt.Peer.SessionID}) sent data({evnt.Packet.DataLength})");
                            break;
                        default:
                            break;
                    }
                    if (result < 0)
                    {
                        Console.Out.WriteLine("[Server] Error!");
                    }
                } while (result > 0);
            }

        }

        public class Client
        {
            public ENetHost client;
            public ENetPeer peer;
            public Client()
            {
                client = ENetHost.Create(null, 1, 0, 0);
            }

            public void Connect(ENetAddress address)
            {
                peer = client.Connect(address, 8);
            }

            public void RunOnce()
            {
                int result;
                do
                {
                    var evnt = new ENetEvent();
                    result = client.HostService(evnt, 100);
                    switch (evnt.Type)
                    {
                        case ENetEventType.CONNECT:
                            Console.WriteLine($"[Client] Peer ({evnt.Peer.SessionID}) connected!");
                            break;
                        case ENetEventType.DISCONNECT:
                            Console.WriteLine($"[Client] Peer ({evnt.Peer.SessionID}) diconnected!");
                            break;
                        case ENetEventType.RECEIVE:
                            Console.WriteLine($"[Client] Peer ({evnt.Peer.SessionID}) sent data({evnt.Packet.DataLength})");
                            break;
                        default:
                            break;
                    }
                    if (result < 0)
                    {
                        Console.Out.WriteLine("[Client] Error!");
                    }
                } while (result > 0);
            }
        }


        static void Main(string[] args)
        {
            var address = new ENetAddress("127.0.0.1", 5005);
            Console.Out.WriteLine($"Hosting on: {address.Host}:{address.Port}");
            var server = new Server(address);
            var client = new Client();
            client.Connect(address);

            /*
            new Thread(() =>
            {
                for(; ; ) { server.RunOnce(); }
            }).Start();

            new Thread(() =>
            {
                Thread.Sleep(500);
                for (; ; ) { client.RunOnce(); }
            }).Start();
            */

            while (true)
            {
                client.RunOnce();
                server.RunOnce();
            }
        }
    }
}
