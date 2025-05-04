using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Xml.Linq;
using System.Runtime.Intrinsics.Arm;
using System;
using System.Reflection;
using static System.Net.Mime.MediaTypeNames;

namespace Tracert
{
    internal class Program
    {
        private static IPEndPoint brEP;
        private static RemoteUser me = new RemoteUser();

        private static Task udpHandler = new Task(() => UdpHandler());
        private static Task tcpHandler = new Task(() => TcpHandler());
        private static Task tcpAccepter = new Task(() => TcpAccepter());
        private static List<RemoteUser> remoteUsers = new List<RemoteUser>();
        private static List<string> journal = new List<string>();
        private static bool alive = true;

        static void Main(string[] args)
        {
            Configure(args);

            udpHandler.Start();
            tcpAccepter.Start();
            tcpHandler.Start();

            while (true)
            {
                string input = Console.ReadLine();

                if (input.EndsWith("-q"))
                {
                    alive = false;
                    for (int i = 0; i < remoteUsers.Count; i++)
                    {
                        RemoteUser ru = remoteUsers[i];
                        ru.SendMsg(4, "");
                        ru.Soc.Close();
                        remoteUsers.Remove(ru);
                        i--;
                    }
                    me.Soc.Close();
                    break;
                }

                if (input.EndsWith("-s"))
                {
                    string text = input.Remove(input.Length - 2);
                    Log(String.Format("Message:\r\n{0}\r\n{1}", me, text), LINE_2);
                    for (int i = 0; i < remoteUsers.Count; i++)
                    {
                        RemoteUser ru = remoteUsers[i];
                        ru.SendMsg(1, text);
                    }
                    continue;
                }

                if (input.EndsWith("-e"))
                {
                    Print("Log emptied successfully", LINE_2);
                    journal.Clear();
                    continue;
                }

                if (input.EndsWith("-j"))
                {
                    if (remoteUsers.Count > 0)
                    {
                        RemoteUser ru = remoteUsers[0];
                        Log(String.Format("User\r\n{0}\r\ndemanded the journal from\r\n{1}", me, ru), LINE_2);
                        ru.SendMsg(5, "");
                    }
                    continue;
                }

                if (input.EndsWith("-l"))
                {
                    for (int i = 0; i < journal.Count; i++)
                    {
                        Print(journal[i]);
                    }
                    continue;
                }

                if (input.EndsWith("-c"))
                {
                    Console.Clear();
                    continue;
                }

            }
        }

        static void UdpHandler()
        {
            Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(me.IP, brEP.Port));

            udp.SendTo(Encoding.UTF8.GetBytes(me.ToString()), brEP);

            byte[] buffer = new byte[256];

            EndPoint rvEP = me.EP;
            udp.ReceiveFrom(buffer, ref rvEP);

            while (alive)
            {
                rvEP = brEP;
                int count = udp.ReceiveFrom(buffer, ref rvEP);

                string userData = Encoding.UTF8.GetString(buffer, 0, count);
                try
                {
                    RemoteUser ru = RemoteUser.Parse(userData);
                    ru.Connect();
                    remoteUsers.Add(ru);
                }
                catch { }
            }
        }

        static void TcpHandler()
        {
            while (alive)
            {
                for (int i = remoteUsers.Count - 1; i >= 0; i--)
                {
                    RemoteUser ru = remoteUsers[i];
                    ru.ReceiveMsg();
                    if (!ru.Soc.Connected)
                    {
                        Log(String.Format("User\r\n{0}\r\ndisconnected", ru));
                        ru.Soc.Close();
                        remoteUsers.RemoveAt(i);
                    }
                }
            }
        }

        static void TcpAccepter()
        {
            while (alive)
            {
                RemoteUser ru = new RemoteUser();
                ru.Soc = me.Soc.Accept();
                ru.Accept();
                remoteUsers.Add(ru);
            }
        }

        static void ProcessMessage(RemoteUser sender, int type, string text)
        {
            switch (type)
            {
                case 1:
                    Log(String.Format("Message:\r\n{0}\r\n{1}", sender, text));
                    break;
                case 2:
                    if (sender.Name != null && sender.Name.Length > 0)
                    {
                        Log(String.Format("User\r\n{0}\r\nchanged name to\r\n{1}", sender, text));
                        sender.Name = text;
                    }
                    else
                    {
                        sender.Name = text;
                        Log(String.Format("User\r\n{0}\r\nconnected", sender));
                    }
                    break;
                case 3:
                    break;
                case 4:
                    sender.Soc.Close();
                    break;
                case 5:
                    Log(String.Format("User\r\n{0}\r\ndemanded the journal", sender));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < journal.Count; i++)
                    {
                        sb.Append(journal[i]);
                        sb.Append("\r\n\r\n");
                    }
                    sender.SendMsg(6, sb.ToString());
                    break;
                case 6:
                    Log(String.Format("User\r\n{0}\r\nprovided the journal", sender));
                    int index = 0;
                    int next = 0;
                    while (index < text.Length)
                    {
                        next = text.IndexOf("\r\n\r\n", index);
                        if (next == -1) { next = text.Length; }
                        Log(text.Substring(index, next - index));
                        index = next + 4;
                    }
                    break;
                default:
                    break;
            }
        }

        private const string LINE_1 = "======================================================================";
        private const string LINE_2 = "**********************************************************************";
        static void Log(string log, string line = LINE_1)
        {
            log = String.Format("{0}\r\n{1}", DateTime.Now, log);
            Print(log, line);
            journal.Add(log);
        }

        static void Print(string log, string line = LINE_1)
        {
            Console.WriteLine("\n{0}\n{1}\n{0}\n", line, log);
        }

        static void Configure(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("Usage:\n\tchat username userIP:userPort broadcastIP:broadcastPort receiveTimeout");
                Environment.Exit(0);
            }

            me.Name = args[0];
            me.EP = IPEndPoint.Parse(args[1]);
            me.IP = me.EP.Address;

            brEP = IPEndPoint.Parse(args[2]);

            me.Soc = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            me.Soc.Bind(me.EP);
            me.Soc.Listen();

            me.EP = (IPEndPoint)me.Soc.LocalEndPoint;

            me.Soc.ReceiveTimeout = int.Parse(args[3]);

            Console.WriteLine("\n{0}\nWelcome to the Chat\n{1}\n{0}\n{2}\n{3}\n{4}\n{5}\n{6}\n{7}\n{8}\n{0}\n", LINE_2, me,
                              "To perform an action, add the corresponding key at the end of the line",
                              "-q\texit the program",
                              "-s\tsend a message",
                              "-e\tclear log",
                              "-j\trequest log",
                              "-l\toutput log",
                              "-c\tclear screen");
            
        }

        class RemoteUser
        {
            public string Name;
            public IPAddress IP;
            public IPEndPoint EP;
            public Socket Soc;

            public static RemoteUser Parse(string data)
            {
                string[] userConfigs = data.Split("\r\n");
                RemoteUser user = new RemoteUser();

                user.Name = userConfigs[1];

                user.EP = IPEndPoint.Parse(userConfigs[0]);
                user.IP = user.EP.Address;

                return user;
            }

            public override string ToString()
            {
                return String.Format("{0}\r\n{1}", EP.ToString(), Name);
            }

            public void Accept()
            {
                EP = (IPEndPoint)Soc.RemoteEndPoint;
                IP = EP.Address;

                Soc.ReceiveTimeout = me.Soc.ReceiveTimeout;
            }

            public void Connect()
            {
                Soc = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Soc.Bind(new IPEndPoint(me.IP, 0));
                Soc.Connect(EP);

                Soc.ReceiveTimeout = me.Soc.ReceiveTimeout;

                SendMsg(2, me.Name);
                Log(String.Format("User\r\n{0}\r\nconnected", this));
            }

            public void SendMsg(int type, string text)
            {
                int count = Encoding.UTF8.GetByteCount(text);
                byte[] buffer = new byte[5 + count];

                buffer[0] = (byte)(type);

                buffer[1] = (byte)(count >> 24);
                buffer[2] = (byte)(count >> 16);
                buffer[3] = (byte)(count >> 8);
                buffer[4] = (byte)(count);

                Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 5);

                Soc.Send(buffer);
            }

            private StringBuilder message = new StringBuilder();
            private int headerCount;
            private int messageType;
            private int messageLeft;
            private static byte[] receiveBuffer = new byte[4096];
            public void ReceiveMsg()
            {
                int offset = 0;
                int readed;
                try { readed = Soc.Receive(receiveBuffer); }
                catch { return; }
                
                while (offset < readed)
                {
                    if (headerCount == 0)
                    {
                        messageType = receiveBuffer[offset++];
                        headerCount++;
                    }
                    while (headerCount < 5 && offset < readed)
                    {
                        messageLeft = (messageLeft << 8) | (receiveBuffer[offset++]);
                        headerCount++;
                    }

                    int msgCount = Math.Min(messageLeft, readed - offset);
                    message.Append(Encoding.UTF8.GetString(receiveBuffer, offset, msgCount));
                    messageLeft -= msgCount;
                    offset += msgCount;

                    if (messageLeft == 0)
                    {
                        ProcessMessage(this, messageType, message.ToString());
                        message.Clear();
                        headerCount = 0;
                    }
                }
            }

        }

    }

}