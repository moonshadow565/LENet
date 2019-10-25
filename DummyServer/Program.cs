using LENet;
using System;

namespace DummyServer
{
    class Program
    {
        public class ENet
        {
            public ENetHost host;
            public ENetPeer peer;
            public string name;
            int iteration = 0;

            public int RunLoop(int timeout = 8)
            {
                int result;
                do
                {
                    result = RunOnce(timeout);
                } while (result > 0);
                return result;
            }

            public int RunOnce(int timeout = 8)
            {
                int result;
                Console.Out.WriteLine($"[{name}] Step: {iteration++}");
                var evnt = new ENetEvent();
                result = host.HostService(evnt, (uint)timeout);
                switch (evnt.Type)
                {
                    case ENetEventType.CONNECT:
                        Console.WriteLine($"[{name}] Peer ({evnt.Peer.SessionID}) connected!");
                        peer = evnt.Peer;
                        break;
                    case ENetEventType.DISCONNECT:
                        Console.WriteLine($"[{name}] Peer ({evnt.Peer.SessionID}) diconnected!");
                        break;
                    case ENetEventType.RECEIVE:
                        Console.WriteLine($"[{name}] Peer ({evnt.Peer.SessionID}) sent data({evnt.Packet.DataLength})");

                        break;
                    default:
                        break;
                }
                if (result < 0)
                {
                    Console.Out.WriteLine($"[{name}] Error!");
                }
                return result;
            }
        }

        static void TestAsync()
        {
            var address = new ENetAddress("127.0.0.1", 5005);
            var server = new ENet { name = "Server" };
            var client = new ENet { name = "Client" };

            server.host = ENetHost.Create(new ENetVersion.Patch420(), address, 32, 0, 0);

            client.host = ENetHost.Create(new ENetVersion.Patch420(), null, 1, 0, 0);
            client.peer = client.host.Connect(address, 8);


            client.RunLoop();
            server.RunLoop();
            client.RunLoop();
            server.RunLoop();

            client.peer.Send(0, new ENetPacket
            {
                Flags = ENetPacketFlags.Reliable,
                Data = new byte[42]
            });

            client.RunLoop();
            server.RunLoop();

            client.peer.Send(0, new ENetPacket
            {
                Flags = ENetPacketFlags.Reliable,
                Data = new byte[69]
            });

            client.RunOnce();
            server.RunOnce();
        }

        static void TestClient()
        {
            var address = new ENetAddress("127.0.0.1", 5005);
            var client = new ENet { name = "Client" };
            client.host = ENetHost.Create(new ENetVersion.Patch420(), null, 1, 0, 0);
            client.host.Connect(address, 8);

            for(; ; )
            {
                client.RunLoop(100);
                client.peer.Send(0, new ENetPacket
                {
                    Flags = ENetPacketFlags.ReliableUnsequenced,
                    Data = new byte[9001]
                });
            }
        }

        static void TestServer()
        {
            var address = new ENetAddress("127.0.0.1", 5005);
            var server = new ENet { name = "Server" };
            server.host = ENetHost.Create(new ENetVersion.Patch420(), address, 1, 0, 0);

            for (; ; )
            {
                server.RunLoop(100);
                if (server.peer != null)
                {
                    server.RunLoop(100);
                    server.peer.Send(0, new ENetPacket
                    {
                        Flags = ENetPacketFlags.Reliable,
                        Data = new byte[1337]
                    });
                }
            }
        }

        static void Main(string[] args)
        {
            TestAsync();
        }
    }
}
