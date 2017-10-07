using PcapDotNet.Base;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Arp;
using PcapDotNet.Packets.Ethernet;
using PcapDotNet.Packets.Icmp;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using WANT;

/*
 * TODO:
 * NAT
 */




namespace PSIP
{

    public partial class Form2 : Form
    {

        private static System.Timers.Timer aTimer, bTimer, ripTimer, ripUpdateTimer;

        private LivePacketDevice Device1, Device2;
        private PacketCommunicator communicator, communicator2;


        private Thread thrReceive;
        private Thread thrReceive2;

        private bool statistics = true;         // aktivacia statistik ak false - statistiky sa neaktualizuju

        private bool capturing1, capturing2 = false;        // zapnutie/vypnutie zachytavania paketov na 1/2 rozhrani

        //private List<Packet> odoslane_z_1 = new List<Packet>();       // sluzilo na ukladanie odoslanych paketov a ich naslednom odchytavani
        //private List<Packet> odoslane_z_2 = new List<Packet>();       // ked program prestal odchytavat vlastne pakety -- nepotrebne

        private ulong prijate1 = 0, prijate2 = 0, odoslane1 = 0, odoslane2 = 0;     // globalne statistiky celkovo odoslanych/ prijatych paketov

        private int TIMEOUT = -1;           // timenout pri prijimani paketov
        private int PACKET_SIZE = 65536;    // max packet size pri prijimani paketov

        private Status one = new Status();  // statistika pre 1 interface
        private Status two = new Status();  // statistika pre 2 interface
        private static ushort SEQ = 0;      // seq pri odosielani PINGu
        private bool RIP1enable = false;    // zapnute/vypnute RIP na 1 interface
        private bool RIP2enable = false;    // zapnute/vypnute RIP na 2 interface
                                            //private bool ProxyArp = false;


        // added for WANT
        private List<Row> routeTable = new List<Row>();             // route tabulka
        private static String sourceIP, destinationIP, source2IP;   // globalne premenne pre IP adresy na rozhraniach a destination adresu pri ARP requeste (z GUI)
        private List<ArpRow> tableARP = new List<ArpRow>();         // ARP tabulka

        private static Ping[] interface1Pings = new Ping[4];        // pingy odoslane z 1 portu - sluzi pri kontrole pri prijimani odpovede
        private static Ping[] interface2Pings = new Ping[4];        // pingy odoslane z 2 portu - sluzi pri kontrole pri prijimani odpovede



        public Form2(LivePacketDevice device, LivePacketDevice device2)
        {
            InitializeComponent();
            Show();
            Device1 = device;
            Device2 = device2;

            communicator = Device1.Open(PACKET_SIZE, PacketDeviceOpenAttributes.Promiscuous | PacketDeviceOpenAttributes.NoCaptureLocal, TIMEOUT);
            communicator2 = Device2.Open(PACKET_SIZE, PacketDeviceOpenAttributes.Promiscuous | PacketDeviceOpenAttributes.NoCaptureLocal, TIMEOUT);

            thrReceive = new Thread(new ThreadStart(this.Receiving1));
            thrReceive.IsBackground = true;
            thrReceive.Start();
            thrReceive2 = new Thread(new ThreadStart(this.Receiving2));
            thrReceive2.IsBackground = true;
            thrReceive2.Start();

            for (int a = 0; a < 4; a++)
            {
                interface1Pings[a] = new Ping();
                interface2Pings[a] = new Ping();
            }
            SetTimer();
            sourceIP = srcIPtext.Text.ToString();
            source2IP = srcIPtext2.Text.ToString();
            setAddress(1);
            setAddress(2);
        }

        private void SetTimer()
        {
            aTimer = new System.Timers.Timer(1000);     // update MAC tabulky
            aTimer.Elapsed += ArpTableCheck;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;

            bTimer = new System.Timers.Timer(1000);     // vykreslovanie MAC tabulky do GUI
            bTimer.Elapsed += ArpTableDraw;
            bTimer.AutoReset = true;
            bTimer.Enabled = true;

            ripTimer = new System.Timers.Timer(1000);     // vykreslovanie MAC tabulky do GUI
            ripTimer.Elapsed += UpdateRouteTimers;
            ripTimer.AutoReset = true;
            ripTimer.Enabled = true;

            ripUpdateTimer = new System.Timers.Timer(30000);     // vykreslovanie MAC tabulky do GUI
            ripUpdateTimer.Elapsed += UpdateRip;
            ripUpdateTimer.AutoReset = true;
            ripUpdateTimer.Enabled = true;
        }

        private void ArpTableDraw(Object source, ElapsedEventArgs e)
        {
            Invoke(new MethodInvoker(delegate () { ArpView.Items.Clear(); }));
            foreach (ArpRow i in tableARP.ToList())
            {
                try
                {
                    ListViewItem item = new ListViewItem(i.Inter.ToString());
                    item.SubItems.Add(i.MAC1.ToString());
                    item.SubItems.Add(i.IP1.ToString());
                    item.SubItems.Add(i.Time.ToString());
                    Invoke(new MethodInvoker(delegate () { ArpView.Items.Add(item); }));
                }
                catch { }
            }
        }

        private void UpdateRouteTimers(Object source, ElapsedEventArgs e)
        {
            List<Row> route1 = new List<Row>();
            List<Row> route2 = new List<Row>();
            for (int i = routeTable.Count - 1; i >= 0; i--)
            {
                if (routeTable[i].Type.Equals('d'))
                {
                    if (routeTable[i].InvalidTime1.Equals(0))   ////////// 0
                    {
                        routeTable[i].Metric = 15;
                        routeTable[i].HoltDownTime1 -= 1;
                    }
                    else
                    {
                        if (routeTable[i].InvalidTime1.Equals(1))
                        {
                            Row j = new Row();
                            j = routeTable[i];
                            j.Metric = 15;
                            if (routeTable[i].Inter.Equals(1)) { route2.Add(j); }
                            if (routeTable[i].Inter.Equals(2)) { route1.Add(j); }
                        }
                        routeTable[i].InvalidTime1 -= 1;/////////// 1
                    }
                    if (routeTable[i].FlushTime1.Equals(0))
                    {
                        routeTable.RemoveAt(i);
                    }
                    else
                    {
                        routeTable[i].FlushTime1 -= 1;
                    }
                    if (routeTable[i].HoltDownTime1.Equals(0))
                    {
                    }
                }
            }




            if (RIP1enable && (route1.Count > 0))
            {
                sendVia1(BuildRipUpdate(Device1, 2, route1));
            }
            if (RIP2enable && (route2.Count > 0))
            {
                sendVia2(BuildRipUpdate(Device2, 1, route2));
            }



            drawRouteTable();
        }
        private void UpdateRip(Object source, ElapsedEventArgs e)
        {
            List<Row> route1 = new List<Row>();
            List<Row> route2 = new List<Row>();

            foreach (Row r in routeTable)
            {
                if (r.Type.Equals('d') && r.Inter.Equals(1)) { route2.Add(r); }
                if (r.Type.Equals('d') && r.Inter.Equals(2)) { route1.Add(r); }

                if (r.Type.Equals('c') && r.Inter.Equals(1) && RIP1enable)
                {
                    Row y = new Row();
                    y = r;
                    y.Metric = 1;
                    y.Next_hop = new IpV4Address("0.0.0.0");
                    route2.Add(y);
                }
                if (r.Type.Equals('c') && r.Inter.Equals(2) && RIP2enable)
                {
                    Row y = new Row();
                    y = r;
                    y.Metric = 1;
                    y.Next_hop = new IpV4Address("0.0.0.0");
                    route1.Add(r);
                }

                if (RIP1enable && route1.Count.Equals(25))
                {
                    sendVia1(BuildRipUpdate(Device1, 2, route1));
                    route1.Clear();
                }
                if (RIP2enable && route2.Count.Equals(25))
                {
                    sendVia2(BuildRipUpdate(Device2, 1, route2));
                    route2.Clear();
                }

            }
            if (RIP1enable && (route1.Count > 0))
            {
                sendVia1(BuildRipUpdate(Device1, 2, route1));
            }
            if (RIP2enable && (route2.Count > 0))
            {
                sendVia2(BuildRipUpdate(Device2, 1, route2));
            }
        }


        public static string GetSubnetMask(byte subnet)
        {
            long mask = (0xffffffffL << (32 - subnet)) & 0xffffffffL;
            mask = IPAddress.HostToNetworkOrder((int)mask);
            return new IPAddress((UInt32)mask).ToString();
        }

        private void drawRouteTable()
        {
            Invoke(new MethodInvoker(delegate () { routeView.Items.Clear(); }));
            foreach (Row i in routeTable.ToList())
            {
                if (i.Metric == 15) { continue; }
                try
                {
                    ListViewItem item = new ListViewItem(i.Type.ToString());
                    item.SubItems.Add(i.Ip.ToString());
                    item.SubItems.Add(i.Mask.ToString());
                    item.SubItems.Add(i.Inter.ToString());
                    item.SubItems.Add(i.Next_hop.ToString());
                    item.SubItems.Add(i.InvalidTime1.ToString());
                    item.SubItems.Add(i.FlushTime1.ToString());
                    item.SubItems.Add(i.HoltDownTime1.ToString());
                    Invoke(new MethodInvoker(delegate () { routeView.Items.Add(item); }));
                }
                catch
                {

                }
            }
        }
        private void ArpTableCheck(Object source, ElapsedEventArgs e)
        {
            for (int i = tableARP.Count - 1; i >= 0; i--)
            {
                if (tableARP[i].Time <= 0)
                {
                    tableARP.RemoveAt(i);
                }
                else
                {
                    tableARP[i].Time -= 1;
                }
            }
            updateStats();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
        private void PacketHandler(Packet packet)
        {
            if (capturing1 == false)
            {
                return;
            }
            if (capturing1)
            {
                if (statistics)
                {
                    prijate1++;
                    updateStatsIn(1, packet);               // update statistics
                }

                IpV4Address dstIP;
                if (packet.Ethernet.EtherType == EthernetType.Arp)
                {
                    dstIP = packet.Ethernet.Arp.TargetProtocolIpV4Address;
                }
                else
                {
                    dstIP = packet.Ethernet.IpV4.Destination;
                }

                IpV4Address myIP = new IpV4Address(sourceIP);
                IpV4Address RIPIP = new IpV4Address("224.0.0.9");

                if (dstIP.Equals(myIP))         // je urcena mojej IP a idem ju spracovat
                {
                    if (packet.Ethernet.EtherType == EthernetType.Arp)                              // ARP
                    {
                        byte[] senderMACbyte = packet.Ethernet.Arp.SenderHardwareAddress.ToArray();
                        String senderMAC = (BitConverter.ToString(senderMACbyte)).Replace("-", ":");
                        MacAddress sendMac = new MacAddress(senderMAC);
                        IpV4Address sendIp = packet.Ethernet.Arp.SenderProtocolIpV4Address;

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Reply"))   // REPLY
                        {
                            addToArpTable(sendMac, sendIp, 1);
                        }

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Request"))     // ARP REQUEST
                        {
                            addToArpTable(sendMac, sendIp, 1);
                            sendVia1(BuildArpPacketReply(Device1, sendMac, sendIp, 1));
                        }
                    }


                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.InternetControlMessageProtocol)
                    {
                        string typ = packet.Ethernet.IpV4.Icmp.MessageTypeAndCode.ToString();

                        if (typ.Equals("Echo"))             // request ICMP
                        {
                            sendVia1(BuildPingReply(1, packet));  // send PING reply
                        }
                        else if (typ.Equals("EchoReply"))  // reply ICMP - spracuj ping
                        {
                            // pozri do zoznamu odoslanych pingov, vypocitaj cas,  a nastav cas do gui
                            IcmpEchoReplyLayer p = (IcmpEchoReplyLayer)packet.Ethernet.IpV4.Icmp.ExtractLayer();
                            ushort ide = p.Identifier;
                            ushort seq = p.SequenceNumber;
                            for (int a = 0; a < 4; a++)
                            {
                                ushort x = interface1Pings[a].Identifier;
                                if (interface1Pings[a].Identifier.Equals(ide) && interface1Pings[a].SequenceNumber1.Equals(seq))
                                {
                                    int ActTime = packet.Timestamp.Millisecond + (1000 * (packet.Timestamp.Second));

                                    int delta = interface1Pings[a].Time.Millisecond + (1000 * (interface1Pings[a].Time.Second));
                                    int difer = ActTime - delta;
                                    switch (a)
                                    {
                                        case 0:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping1.Text = difer.ToString();
                                            }));

                                            break;
                                        case 1:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping2.Text = difer.ToString();
                                            }));
                                            break;
                                        case 2:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping3.Text = difer.ToString();
                                            }));
                                            break;
                                        case 3:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping4.Text = difer.ToString();
                                            }));
                                            break;
                                        default:
                                            Console.WriteLine("Ping reply not received");
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    // iny protokol
                }
                else if ((dstIP.Equals(RIPIP)) && RIP1enable)
                {
                    //Console.WriteLine("Prijal som RIP paket");
                    MacAddress myMac = Device1.GetMacAddress();
                    MacAddress targetMac = packet.Ethernet.Destination;


                    int ttl = packet.Ethernet.IpV4.Ttl;
                    //Console.WriteLine("TTL: " + ttl);

                    String payload = packet.Ethernet.IpV4.Udp.Payload.ToHexadecimalString();    // RIP payload
                    int payloadLength = payload.Length;
                    String RipHeader = payload.Substring(0, 8);                                 // nepotrebna RIP hlavicka
                    String RIPRoutes = payload.Substring(8, payloadLength - 8);                 // samotne routy v surovom stave, jedna za druhou
                    int RIPRoutesLength = RIPRoutes.Length;
                    String command = RipHeader.Substring(0, 2);
                    int com = Int32.Parse(command);
                    IpV4Address srcIP = packet.Ethernet.IpV4.Source;

                    if (ttl >= 1 && com == 2)        // response = spracujem packet
                    {

                        int i = 0;
                        while ((i + 1) * 40 <= RIPRoutesLength) // prechadza vsetky route zaznamy v pakete
                        {
                            String route = RIPRoutes.Substring(i * 40, 40);     // jedna routa 

                            // vytiahne jednotlive komponenty samotnej routy
                            String family = route.Substring(0, 4);
                            String tag = route.Substring(4, 4);
                            String IP = route.Substring(8, 8);
                            String mask = route.Substring(16, 8);
                            String next_hop = route.Substring(24, 8);
                            String metric = route.Substring(38, 2);

                            // prevod IP adries z HEX tvaru do DEC (DEC.DEC.DEC.DEC)
                            String ajpi = Convert.ToString(Convert.ToInt32(IP.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(IP.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(IP.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(IP.Substring(6, 2), 16));
                            String next = Convert.ToString(Convert.ToInt32(next_hop.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(6, 2), 16));

                            // prevod z DEC na IP
                            IpV4Address ip = new IpV4Address(ajpi);
                            IpV4Address nextHop = new IpV4Address(next);
                            IpV4Address nullIP = new IpV4Address("0.0.0.0");
                            if (nextHop.Equals(nullIP))
                            {
                                nextHop = srcIP;
                            }

                            // pocet jednotiek v maske teda pre 255.255.255.0 vrati 24
                            int c = BitCount(mask.ToCharArray());

                            // metrika z 8 bitov spravi INT

                            int newMet = Convert.ToInt32(metric, 16);
                            addRoute('d', ip, c, nextHop, newMet, 1); // 1 = rip prijaty na 1 int

                            i++;
                        }
                    }
                    else if (ttl >= 1 && com == 1)
                    {
                        int i = 0;
                        while ((i + 1) * 40 <= RIPRoutesLength) // prechadza vsetky route zaznamy v pakete
                        {
                            String route = RIPRoutes.Substring(i * 40, 40);     // jedna routa 

                            String mask = route.Substring(16, 8);
                            String next_hop = route.Substring(24, 8);
                            IpV4Address nul = new IpV4Address("0.0.0.0");
                            String m = Convert.ToString(Convert.ToInt32(mask.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(mask.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(mask.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(mask.Substring(6, 2), 16));
                            String n = Convert.ToString(Convert.ToInt32(next_hop.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(6, 2), 16));



                            IpV4Address maska = new IpV4Address(m);
                            IpV4Address adresa = new IpV4Address(n);
                            if (maska.Equals(nul) && adresa.Equals(nul))
                            {
                                Thread p = new Thread(new ThreadStart(() => RIPAnswer(2)));
                                p.IsBackground = true;
                                p.Start();
                            }
                            i++;
                        }
                    }
                }
                else
                {
                    if (packet.Ethernet.EtherType == EthernetType.Arp)
                    {
                        byte[] senderMACbyte = packet.Ethernet.Arp.SenderHardwareAddress.ToArray();
                        String senderMAC = (BitConverter.ToString(senderMACbyte)).Replace("-", ":");
                        MacAddress sendMac = new MacAddress(senderMAC);
                        IpV4Address sendIp = packet.Ethernet.Arp.SenderProtocolIpV4Address;

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Reply"))   // REPLY
                        {
                            addToArpTable(sendMac, sendIp, 1);
                        }

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Request"))     // ARP REQUEST
                        {
                            Row Aroute;
                            if ((Aroute = findNextHop(dstIP)) != null)
                            {
                                if (Aroute.Inter.Equals(2))
                                {
                                    byte[] senderMACbyte2 = packet.Ethernet.Arp.SenderHardwareAddress.ToArray();
                                    String senderMAC2 = (BitConverter.ToString(senderMACbyte2)).Replace("-", ":");
                                    MacAddress sendMac2 = new MacAddress(senderMAC2);

                                    sendVia1(BuildArpProxyReply(Device1, dstIP, sendMac2, packet.Ethernet.Arp.SenderProtocolIpV4Address, 1));
                                }
                            }
                        }
                        return;
                    }

                    Thread s = new Thread(new ThreadStart(() => reSend(packet, dstIP, 1)));
                    s.IsBackground = true;
                    s.Start();



                }// nie je urcena mojej IP a idem ju preposlat

            }


        }


        private void reSend(Packet packet, IpV4Address dstIP, int incPort)
        {
            IpV4Address nullNext_Hop = new IpV4Address("0.0.0.0");
            int ttl = packet.Ethernet.IpV4.Ttl;
            if (ttl >= 1)
            {
                Row route;
                if ((route = findNextHop(dstIP)) != null)
                {
                    if (!(route.Next_hop.Equals(nullNext_Hop)))                           // jedna sa o smerovanie pomocou nexthopu
                    {
                        ArpRow arp = findInArpTable(route.Next_hop);
                        if (arp == null)                            // nemam v ARP tabulke zaznam o MAC adrese targetu
                        {
                            try { sendVia1(BuildArpPacketRequest(Device1, 1, route.Next_hop.ToString())); } catch {; }
                            try { sendVia2(BuildArpPacketRequest(Device2, 2, route.Next_hop.ToString())); } catch {; }

                            int i = 4;
                            while ((arp = findInArpTable(route.Next_hop)) == null && 0 < i)
                            {
                                Thread.Sleep(100);
                                i--;
                            }
                        }
                        if (arp != null)
                        {                                           // mam cielovu IP aj cielovu MAC preposielam paket
                            if (arp.Inter == 1 && incPort == 2)
                            {
                                try { sendVia1(sendPacket(1, packet, arp, route)); } catch {; }
                            }
                            else if (arp.Inter == 2 && incPort == 1)
                            {
                                try { sendVia2(sendPacket(2, packet, arp, route)); } catch {; }
                            }
                        }
                    }
                    else if (route.Next_hop.Equals(nullNext_Hop))  // smerujem pomocou rozhrani, lebo nexthop = 0.0.0.0
                    {
                        ArpRow arp = findInArpTable(dstIP);
                        if (arp == null)                            // nemam v ARP tabulke zaznam o MAC adrese targetu
                        {
                            if (route.Inter == 1 && incPort == 2)
                            {
                                try { sendVia1(BuildArpPacketRequest(Device1, 1, dstIP.ToString())); } catch {; }
                            }
                            else if (route.Inter == 2 && incPort == 1)
                            {
                                try { sendVia2(BuildArpPacketRequest(Device2, 2, dstIP.ToString())); } catch {; }
                            }

                            int i = 4;
                            while ((arp = findInArpTable(dstIP)) == null && 0 < i)
                            {
                                i--;
                                Thread.Sleep(100);
                            }
                        }
                        if (arp != null)
                        {                                           // mam cielovu IP aj cielovu MAC preposielam paket
                            if (arp.Inter == 1 && incPort == 2)
                            {
                                try { sendVia1(sendPacket(1, packet, arp, route)); } catch {; }
                            }
                            else if (arp.Inter == 2 && incPort == 1)
                            {
                                try { sendVia2(sendPacket(2, packet, arp, route)); } catch {; }
                            }
                        }

                    }

                }
            }
        }
        private Packet BuildPingReply(int i, Packet packet)
        {
            IpV4Address srcIP;
            MacAddress srcMac;
            if (i == 1)
            {
                srcIP = new IpV4Address(sourceIP);
                srcMac = Device1.GetMacAddress();
            }
            else
            {
                srcIP = new IpV4Address(source2IP);
                srcMac = Device2.GetMacAddress();
            }
            EthernetLayer ethernetLayer = (EthernetLayer)packet.Ethernet.ExtractLayer();
            IpV4Layer ipV4Layer = (IpV4Layer)packet.Ethernet.IpV4.ExtractLayer();

            ethernetLayer.Source = srcMac;
            ethernetLayer.Destination = packet.Ethernet.Source;
            ethernetLayer.EtherType = EthernetType.None;

            Random rand = new Random();
            ipV4Layer.Source = srcIP;
            ipV4Layer.CurrentDestination = packet.Ethernet.IpV4.Source;
            ipV4Layer.HeaderChecksum = null;
            ipV4Layer.Ttl = 128;
            ipV4Layer.Identification = (ushort)rand.Next(0, 65535);
            ipV4Layer.TypeOfService = 0;

            IcmpIdentifiedDatagram icmp = (IcmpIdentifiedDatagram)packet.Ethernet.IpV4.Icmp;
            IcmpEchoReplyLayer icmpLayer = new IcmpEchoReplyLayer();
            icmpLayer.SequenceNumber = icmp.SequenceNumber; //switchID
            icmpLayer.Identifier = icmp.Identifier; //switchID

            PayloadLayer payload = (PayloadLayer)packet.Ethernet.IpV4.Icmp.Payload.ExtractLayer();


            PacketBuilder builder = new PacketBuilder(ethernetLayer, ipV4Layer, icmpLayer, payload);

            return builder.Build(DateTime.Now);
        }

        private Packet sendPacket(int v, Packet packet, ArpRow arp, Row route)
        {
            //IpV4Address srcIP;
            MacAddress srcMac;

            MacAddress dstMac = arp.MAC1;
            if (v == 1)
            {
                //srcIP = new IpV4Address(sourceIP);
                srcMac = Device1.GetMacAddress();
                //byte[] sourceMacByte = myMac.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            }
            else
            {
                //srcIP = new IpV4Address(source2IP);
                srcMac = Device2.GetMacAddress();
            }

            EthernetLayer eth = (EthernetLayer)packet.Ethernet.ExtractLayer();
            IpV4Layer ipv4 = (IpV4Layer)packet.Ethernet.IpV4.ExtractLayer();
            PayloadLayer payload = (PayloadLayer)packet.Ethernet.IpV4.Payload.ExtractLayer();

            eth.Source = srcMac;
            eth.Destination = dstMac;
            eth.EtherType = EthernetType.None;


            ipv4.Ttl -= 1;
            ipv4.Fragmentation = IpV4Fragmentation.None;
            //ipv4.Protocol = null;
            ipv4.TypeOfService = 0;
            //ipv4.Source = srcIP;
            ipv4.HeaderChecksum = null; // automatic

            PacketBuilder builder = new PacketBuilder(eth, ipv4, payload);
            return builder.Build(DateTime.Now);
        }

        private Row findNextHop(IpV4Address dstIP)
        {
            Row best = null;
            foreach (Row r in routeTable)
            {
                if (r.Metric.Equals(15)) { continue; }
                IpV4Address actualIP = new IpV4Address();
                IpV4Address bestIP = new IpV4Address();
                actualIP = r.Ip;
                bestIP = dstIP;
                int mask = r.Mask;

                string[] w = actualIP.ToString().Split('.');
                string[] v = bestIP.ToString().Split('.');

                string r1 = Convert.ToString(Int32.Parse(w[0]), 2).PadLeft(8, '0');
                string r2 = Convert.ToString(Int32.Parse(w[1]), 2).PadLeft(8, '0');
                string r3 = Convert.ToString(Int32.Parse(w[2]), 2).PadLeft(8, '0');
                string r4 = Convert.ToString(Int32.Parse(w[3]), 2).PadLeft(8, '0');

                string actString = r1 + "" + r2 + "" + r3 + "" + r4;

                string s1 = Convert.ToString(Int32.Parse(v[0]), 2).PadLeft(8, '0');
                string s2 = Convert.ToString(Int32.Parse(v[1]), 2).PadLeft(8, '0');
                string s3 = Convert.ToString(Int32.Parse(v[2]), 2).PadLeft(8, '0');
                string s4 = Convert.ToString(Int32.Parse(v[3]), 2).PadLeft(8, '0');

                string bestString = s1 + "" + s2 + "" + s3 + "" + s4;

                string actSub = actString.Substring(0, mask);
                string bestSub = bestString.Substring(0, mask);

                if (actSub.Equals(bestSub))
                {
                    if (best == null)
                    {
                        best = r;
                        continue;
                    }

                    if (r.Mask < best.Mask)
                    {
                        continue;
                    }
                    else if (r.Mask == best.Mask)
                    {
                        if (best.Type.Equals('d') && (r.Type.Equals('c') || r.Type.Equals('s')))
                        {
                            best = r;
                            continue;
                        }
                        if ((best.Type.Equals('s') && r.Type.Equals('c')))
                        {
                            best = r;
                            continue;
                        }
                    }
                    else if (r.Mask > best.Mask)
                    {
                        best = r;
                        continue;
                    }
                }
            }
            return best;
        }

        public static int BitCount(char[] m)                       // pocita masku z x.x.x.x na /XX hodnotu
        {
            int x = 0;
            for (int i = 0; i < m.Length; i++)
            {
                if (m[i] == '8') x += 1;
                if (m[i] == 'c') x += 2;
                if (m[i] == 'e') x += 3;
                if (m[i] == 'f') x += 4;
            }
            return x;
        }

        private ArpRow findInArpTable(IpV4Address sendIp)       // DOKONCIT vyhladavanie v ARP tabulke
        {
            ArpRow x = null;
            foreach (ArpRow i in tableARP.ToList())
            {
                if (sendIp.Equals(i.IP1))
                {
                    return i;
                }
            }
            return x;
        }

        void addToArpTable(MacAddress mac, IpV4Address ip, int port)
        {
            foreach (ArpRow i in tableARP.ToList())
            {
                if (i.IP1.Equals(ip))   // zaznam ma rovnaku MAC aj IP - UPDETE TIMER
                {
                    i.Time = int.Parse(ARPTime.Value.ToString());
                    i.MAC1 = mac;
                    i.Inter = port;
                    return;
                }

            }
            ArpRow newrow = new ArpRow();
            newrow.MAC1 = mac;
            newrow.IP1 = ip;
            newrow.Inter = port;
            newrow.Time = int.Parse(ARPTime.Value.ToString());
            //Console.WriteLine("Pridavam do ARP tabulky " + newrow.MAC1 + " -- " + newrow.IP1 + " -- " + newrow.Inter + " !");
            tableARP.Add(newrow);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
        private void PacketHandler2(Packet packet)
        {
            if (capturing2 == false)
            {
                return;
            }
            if (capturing2)
            {
                if (statistics)
                {
                    prijate2++;
                    updateStatsIn(2, packet);               // update statistics
                }

                IpV4Address dstIP;
                if (packet.Ethernet.EtherType == EthernetType.Arp)
                {
                    dstIP = packet.Ethernet.Arp.TargetProtocolIpV4Address;
                }
                else
                {
                    dstIP = packet.Ethernet.IpV4.Destination;
                }

                IpV4Address myIP = new IpV4Address(source2IP);
                IpV4Address RIPIP = new IpV4Address("224.0.0.9");

                if (dstIP.Equals(myIP))         // je urcena mojej IP a idem ju spracovat
                {
                    if (packet.Ethernet.EtherType == EthernetType.Arp)                              // ARP
                    {
                        byte[] senderMACbyte = packet.Ethernet.Arp.SenderHardwareAddress.ToArray();
                        String senderMAC = (BitConverter.ToString(senderMACbyte)).Replace("-", ":");
                        MacAddress sendMac = new MacAddress(senderMAC);
                        IpV4Address sendIp = packet.Ethernet.Arp.SenderProtocolIpV4Address;

                        //byte[] senderIPbyte = packet.Ethernet.Arp.SenderProtocolAddress.ToArray();
                        //String senderIP = "" + senderIPbyte[0] + "." + senderIPbyte[1] + "." + senderIPbyte[2] + "." + senderIPbyte[3];

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Reply"))   // REPLY
                        {
                            addToArpTable(sendMac, sendIp, 2);
                        }

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Request"))     // ARP REQUEST
                        {
                            //Console.WriteLine("Spracuvavam ARP request, pretoze je urcena pre: " + dstIP);
                            //Console.WriteLine("Moja IP je " + myIP + "rozhranie 1");
                            addToArpTable(sendMac, sendIp, 2);

                            sendVia2(BuildArpPacketReply(Device2, sendMac, sendIp, 2));
                        }
                    }


                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.InternetControlMessageProtocol)
                    {
                        string typ = packet.Ethernet.IpV4.Icmp.MessageTypeAndCode.ToString();

                        if (typ.Equals("Echo"))             // request ICMP
                        {
                            sendVia2(BuildPingReply(2, packet));  // send PING reply
                        }
                        else if (typ.Equals("EchoReply"))  // reply ICMP - spracuj ping
                        {
                            // pozri do zoznamu odoslanych pingov, vypocitaj cas,  a nastav cas do gui
                            IcmpEchoReplyLayer p = (IcmpEchoReplyLayer)packet.Ethernet.IpV4.Icmp.ExtractLayer();
                            ushort ide = p.Identifier;
                            ushort seq = p.SequenceNumber;

                            for (int a = 0; a < 4; a++)
                            {
                                ushort x = interface2Pings[a].Identifier;
                                if (interface2Pings[a].Identifier.Equals(ide) && interface2Pings[a].SequenceNumber1.Equals(seq))
                                {
                                    int ActTime = packet.Timestamp.Millisecond + (1000 * (packet.Timestamp.Second));

                                    int delta = interface2Pings[a].Time.Millisecond + (1000 * (interface2Pings[a].Time.Second));
                                    int difer = ActTime - delta;
                                    switch (a)
                                    {
                                        case 0:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping1.Text = difer.ToString();
                                            }));

                                            break;
                                        case 1:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping2.Text = difer.ToString();
                                            }));
                                            break;
                                        case 2:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping3.Text = difer.ToString();
                                            }));
                                            break;
                                        case 3:
                                            Invoke(new MethodInvoker(delegate ()
                                            {
                                                ping4.Text = difer.ToString();
                                            }));
                                            break;
                                        default:
                                            Console.WriteLine("Ping reply not received");
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    // tu pridat if(iny protokol)
                }
                else if ((dstIP.Equals(RIPIP)) && RIP2enable)
                {
                    //Console.WriteLine("Prijal som RIP paket");
                    //MacAddress myMac = Device2.GetMacAddress();
                    MacAddress targetMac = packet.Ethernet.Destination;


                    int ttl = packet.Ethernet.IpV4.Ttl;
                    //Console.WriteLine("TTL: " + ttl);

                    String payload = packet.Ethernet.IpV4.Udp.Payload.ToHexadecimalString();    // RIP payload
                    int payloadLength = payload.Length;
                    String RipHeader = payload.Substring(0, 8);                                 // nepotrebna RIP hlavicka
                    String RIPRoutes = payload.Substring(8, payloadLength - 8);                 // samotne routy v surovom stave, jedna za druhou
                    int RIPRoutesLength = RIPRoutes.Length;
                    string command = RipHeader.Substring(0, 2);
                    int com = Int32.Parse(command);
                    IpV4Address srcIP = packet.Ethernet.IpV4.Source;

                    if (ttl >= 1 && com == 2)
                    {

                        int i = 0;
                        while ((i + 1) * 40 <= RIPRoutesLength) // prechadza vsetky route zaznamy v pakete
                        {
                            String route = RIPRoutes.Substring(i * 40, 40);     // jedna routa 

                            // vytiahne jednotlive komponenty samotnej routy
                            String family = route.Substring(0, 4);
                            String tag = route.Substring(4, 4);
                            String IP = route.Substring(8, 8);
                            String mask = route.Substring(16, 8);
                            String next_hop = route.Substring(24, 8);
                            String metric = route.Substring(38, 2);

                            // prevod IP adries z HEX tvaru do DEC (DEC.DEC.DEC.DEC)
                            String ajpi = Convert.ToString(Convert.ToInt32(IP.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(IP.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(IP.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(IP.Substring(6, 2), 16));
                            String next = Convert.ToString(Convert.ToInt32(next_hop.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(6, 2), 16));

                            // prevod z DEC na IP
                            IpV4Address ip = new IpV4Address(ajpi);

                            IpV4Address nextHop = new IpV4Address(next);

                            IpV4Address nullIP = new IpV4Address("0.0.0.0");
                            if (nextHop.Equals(nullIP))
                            {
                                nextHop = srcIP;
                            }
                            // pocet jednotiek v maske teda pre 255.255.255.0 vrati 24
                            int c = BitCount(mask.ToCharArray());

                            // metrika z 8 bitov spravi INT
                            int newMet = Convert.ToInt32(metric, 16);
                            //Int32.TryParse(metric, out met);

                            addRoute('d', ip, c, nextHop, newMet, 2); // 1 = rip prijaty na 1 int

                            i++;
                        }
                    }
                    else if (ttl >= 1 && com == 1)
                    {

                        int i = 0;
                        while ((i + 1) * 40 <= RIPRoutesLength) // prechadza vsetky route zaznamy v pakete
                        {
                            String route = RIPRoutes.Substring(i * 40, 40);     // jedna routa 

                            String mask = route.Substring(16, 8);
                            String next_hop = route.Substring(24, 8);
                            IpV4Address nul = new IpV4Address("0.0.0.0");

                            String m = Convert.ToString(Convert.ToInt32(mask.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(mask.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(mask.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(mask.Substring(6, 2), 16));
                            String n = Convert.ToString(Convert.ToInt32(next_hop.Substring(0, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(2, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(4, 2), 16)) + "." + Convert.ToString(Convert.ToInt32(next_hop.Substring(6, 2), 16));



                            IpV4Address maska = new IpV4Address(m);
                            IpV4Address adresa = new IpV4Address(n);

                            if (maska.Equals(nul) && adresa.Equals(nul))
                            {
                                Thread p = new Thread(new ThreadStart(() => RIPAnswer(1)));
                                p.IsBackground = true;
                                p.Start();
                            }
                            i++;
                        }
                    }

                }
                else
                {
                    if (packet.Ethernet.EtherType == EthernetType.Arp)
                    {   // mam pripojenu dstIP na druhom rozhrani? 
                        // AK ano tak posli odpoved s mojou MAC a DstIP
                        // ak nie tak break? return?
                        byte[] senderMACbyte = packet.Ethernet.Arp.SenderHardwareAddress.ToArray();
                        String senderMAC = (BitConverter.ToString(senderMACbyte)).Replace("-", ":");
                        MacAddress sendMac = new MacAddress(senderMAC);
                        IpV4Address sendIp = packet.Ethernet.Arp.SenderProtocolIpV4Address;

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Reply"))   // REPLY
                        {
                            addToArpTable(sendMac, sendIp, 2);
                        }

                        if (packet.Ethernet.Arp.Operation.ToString().Equals("Request"))     // ARP REQUEST
                        {
                            Row Aroute;
                            if ((Aroute = findNextHop(dstIP)) != null)
                            {
                                if (Aroute.Inter.Equals(1))
                                {
                                    byte[] senderMACbyte2 = packet.Ethernet.Arp.SenderHardwareAddress.ToArray();
                                    String senderMAC2 = (BitConverter.ToString(senderMACbyte2)).Replace("-", ":");
                                    MacAddress sendMac2 = new MacAddress(senderMAC2);

                                    sendVia2(BuildArpProxyReply(Device2, dstIP, sendMac2, packet.Ethernet.Arp.SenderProtocolIpV4Address, 2));
                                }
                            }
                        }
                        return;
                    }


                    Thread s = new Thread(new ThreadStart(() => reSend(packet, dstIP, 2)));
                    s.IsBackground = true;
                    s.Start();

                }// nie je urcena mojej IP a idem ju preposlat

            }


        }

        private void updateStatsIn(int port, Packet packet)
        {
            if (port == 1)
            {
                if (packet.Ethernet.EtherType == EthernetType.Arp)
                {
                    //ARP
                    one.InArp++;
                    return;
                }
                else if (packet.Ethernet.EtherType == EthernetType.IpV4)
                {
                    //IP
                    one.InIp++;
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Udp)
                    {
                        //UDP
                        one.InUdp++;
                        IpV4Address dstIP = packet.Ethernet.IpV4.Destination;
                        IpV4Address RIPIP = new IpV4Address("224.0.0.9");
                        int dstPort = packet.Ethernet.IpV4.Udp.DestinationPort;
                        int srcPort = packet.Ethernet.IpV4.Udp.SourcePort;
                        if ((dstIP.Equals(RIPIP)) && (dstPort.Equals(520)) && (srcPort.Equals(520)))
                        {
                            one.InRip++;
                        }
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Tcp)
                    {
                        //TCP
                        one.InTcp++;
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.InternetControlMessageProtocol)
                    {
                        //ICMP
                        one.InIcmp++;
                        return;
                    }
                    return;
                }
                one.InAll++;
            }
            if (port == 2)
            {

                if (packet.Ethernet.EtherType == EthernetType.Arp)
                {
                    //ARP
                    two.InArp++;
                    return;
                }
                else if (packet.Ethernet.EtherType == EthernetType.IpV4)
                {
                    //IP
                    two.InIp++;
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Udp)
                    {
                        //UDP
                        two.InUdp++;
                        IpV4Address dstIP = packet.Ethernet.IpV4.Destination;
                        IpV4Address RIPIP = new IpV4Address("224.0.0.9");
                        int dstPort = packet.Ethernet.IpV4.Udp.DestinationPort;
                        int srcPort = packet.Ethernet.IpV4.Udp.SourcePort;
                        if ((dstIP.Equals(RIPIP)) && (dstPort.Equals(520)) && (srcPort.Equals(520)))
                        {
                            two.InRip++;
                        }
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Tcp)
                    {
                        //TCP
                        two.InTcp++;
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.InternetControlMessageProtocol)
                    {
                        //ICMP
                        two.InIcmp++;
                        return;
                    }
                    return;
                }
                two.InAll++;
            }
        }
        private void updateStatsOut(int port, Packet packet)
        {
            if (port == 1)
            {
                if (packet.Ethernet.EtherType == EthernetType.Arp)
                {
                    //ARP
                    one.OutArp++;
                    return;
                }
                else if (packet.Ethernet.EtherType == EthernetType.IpV4)
                {
                    //IP
                    one.OutIp++;
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Udp)
                    {
                        //UDP
                        one.OutUdp++;
                        IpV4Address dstIP = packet.Ethernet.IpV4.Destination;
                        IpV4Address RIPIP = new IpV4Address("224.0.0.9");
                        int dstPort = packet.Ethernet.IpV4.Udp.DestinationPort;
                        int srcPort = packet.Ethernet.IpV4.Udp.SourcePort;
                        if ((dstIP.Equals(RIPIP)) && (dstPort.Equals(520)) && (srcPort.Equals(520)))
                        {
                            one.OutRip++;
                        }
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Tcp)
                    {
                        //TCP
                        one.OutTcp++;
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.InternetControlMessageProtocol)
                    {
                        //ICMP
                        one.OutIcmp++;
                        return;
                    }
                    return;
                }
                one.OutAll++;
            }
            if (port == 2)
            {
                if (packet.Ethernet.EtherType == EthernetType.Arp)
                {
                    //ARP
                    two.OutArp++;
                    return;
                }
                else if (packet.Ethernet.EtherType == EthernetType.IpV4)
                {
                    //IP
                    two.OutIp++;
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Udp)
                    {
                        //UDP
                        two.OutUdp++;
                        IpV4Address dstIP = packet.Ethernet.IpV4.Destination;
                        IpV4Address RIPIP = new IpV4Address("224.0.0.9");
                        int dstPort = packet.Ethernet.IpV4.Udp.DestinationPort;
                        int srcPort = packet.Ethernet.IpV4.Udp.SourcePort;
                        if ((dstIP.Equals(RIPIP)) && (dstPort.Equals(520)) && (srcPort.Equals(520)))
                        {
                            two.OutRip++;
                        }
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.Tcp)
                    {
                        //TCP
                        two.OutTcp++;
                        return;
                    }
                    if (packet.Ethernet.IpV4.Protocol == IpV4Protocol.InternetControlMessageProtocol)
                    {
                        //ICMP
                        two.OutIcmp++;
                        return;
                    }
                    return;
                }
                two.OutAll++;
            }
        }
        private void SendPing()
        {

            Invoke(new MethodInvoker(delegate ()
            {
                ping1.Text = "0000";
                ping2.Text = "0000";
                ping3.Text = "0000";
                ping4.Text = "0000";
            }));


            IpV4Address pingIp = new IpV4Address(pingIP.Text);

            Row route;
            IpV4Address nullNext_Hop = new IpV4Address("0.0.0.0");
            for (int x = 0; x < 4; x++)
            {
                if ((route = findNextHop(pingIp)) != null)
                {
                    if (!(route.Next_hop.Equals(nullNext_Hop)))                 // smerovanie pomocou next-hopu
                    {                                                               // routa ma iba next-hop
                        ArpRow arp = findInArpTable(route.Next_hop);

                        if (arp == null)                            // nemam v ARP tabulke zaznam o MAC adrese targetu
                        {
                            int i = 4;
                            sendVia1(BuildArpPacketRequest(Device1, 1, route.Next_hop.ToString()));
                            sendVia2(BuildArpPacketRequest(Device2, 2, route.Next_hop.ToString()));

                            while (((arp = findInArpTable(route.Next_hop)) == null) && 0 < i)
                            {
                                Thread.Sleep(100);
                                i--;
                            }
                        }
                        if (arp != null)
                        {
                            // mam cielovu IP aj cielovu MAC preposielam paket
                            Packet p;
                            if (arp.Inter == 1)
                            {
                                sendVia1(p = BuildIcmpPacket(x, 1, arp, pingIp, Device1.GetMacAddress()));
                                interface1Pings[x].Time = p.Timestamp;
                            }
                            else if (arp.Inter == 2)
                            {
                                sendVia1(p = BuildIcmpPacket(x, 2, arp, pingIp, Device2.GetMacAddress()));
                                interface2Pings[x].Time = p.Timestamp;
                            }
                        }
                    }
                    else if (route.Next_hop.Equals(nullNext_Hop))       // smerujem pomocou rozhrani lebo nexthop = "0.0.0.0"
                    {                                                                                       // routa ma iba interface
                        ArpRow arp = findInArpTable(pingIp);

                        if (arp == null)                            // nemam v ARP tabulke zaznam o MAC adrese targetu
                        {
                            if (route.Inter.Equals(1))
                            {
                                sendVia1(BuildArpPacketRequest(Device1, 1, pingIp.ToString()));
                            }
                            else if (route.Inter.Equals(2))
                            {
                                sendVia2(BuildArpPacketRequest(Device2, 2, pingIp.ToString()));
                            }

                            int i = 4;
                            while (((arp = findInArpTable(pingIp)) == null) && 0 < i)
                            {
                                Thread.Sleep(100);
                                i--;
                            }
                        }
                        if (arp != null)
                        {
                            // mam cielovu IP aj cielovu MAC preposielam paket
                            Packet p;
                            if (arp.Inter == 1)
                            {
                                sendVia1(p = BuildIcmpPacket(x, 1, arp, pingIp, Device1.GetMacAddress()));
                                interface1Pings[x].Time = p.Timestamp;
                            }
                            else if (arp.Inter == 2)
                            {
                                sendVia2(p = BuildIcmpPacket(x, 2, arp, pingIp, Device2.GetMacAddress()));
                                interface2Pings[x].Time = p.Timestamp;
                            }
                        }
                    }
                    else
                    if (route.Inter != 0 && (route.Next_hop.Equals(new IpV4Address("0.0.0.0"))) == false)
                    {                                                                                       // routa ma interface aj next-hop
                        ArpRow arp = findInArpTable(route.Next_hop);

                        if (arp == null)                            // nemam v ARP tabulke zaznam o MAC adrese targetu
                        {
                            if (route.Inter.Equals(1))
                            {
                                sendVia1(BuildArpPacketRequest(Device1, 1, route.Next_hop.ToString()));
                            }
                            else if (route.Inter.Equals(2))
                            {
                                sendVia2(BuildArpPacketRequest(Device2, 2, route.Next_hop.ToString()));
                            }

                            int i = 4;
                            while (((arp = findInArpTable(route.Next_hop)) == null) && 0 < i)
                            {
                                Thread.Sleep(100);
                                i--;
                            }
                        }
                        if (arp != null)
                        {
                            Packet p;
                            if (route.Inter == 1)
                            {
                                sendVia1(p = BuildIcmpPacket(x, 1, arp, route.Next_hop, Device1.GetMacAddress()));
                                interface1Pings[x].Time = p.Timestamp;
                            }
                            else if (route.Inter == 2)
                            {
                                sendVia1(p = BuildIcmpPacket(x, 2, arp, route.Next_hop, Device2.GetMacAddress()));
                                interface2Pings[x].Time = p.Timestamp;
                            }
                        }

                    }

                }
                else
                {

                    if (x == 0)
                    {
                        Invoke(new MethodInvoker(delegate ()
                        {
                            ping1.Text = "xxxx";
                        }));
                    }
                    if (x == 1)
                    {
                        Invoke(new MethodInvoker(delegate ()
                        {
                            ping2.Text = "xxxx";
                        }));
                    }
                    if (x == 2)
                    {
                        Invoke(new MethodInvoker(delegate ()
                        {
                            ping3.Text = "xxxx";
                        }));
                    }
                    if (x == 3)
                    {
                        Invoke(new MethodInvoker(delegate ()
                        {
                            ping4.Text = "xxxx";
                        }));
                    }
                }
                Thread.Sleep(500);  // timeout medzi pingami
            }
        }

        private void Receiving1()
        {
            communicator.ReceivePackets(-1, PacketHandler);
        }
        private void Receiving2()
        {
            communicator2.ReceivePackets(-1, PacketHandler2);
        }


        private void button1_Click_1(object sender, EventArgs e)    // tlacidlo RESET ROUTE TABLE
        {
            tableARP = new List<ArpRow>();    // realloc table = table is clear
        }

        private void updateStats()
        {
            Invoke(new MethodInvoker(delegate ()
            {
                label7.Text = prijate1.ToString();
                label8.Text = odoslane1.ToString();
                label9.Text = prijate2.ToString();
                label10.Text = odoslane2.ToString();

                label62.Text = one.InAll.ToString();
                label21.Text = one.InArp.ToString();
                label22.Text = one.InTcp.ToString();
                label23.Text = one.InUdp.ToString();
                label24.Text = one.InIp.ToString();
                label25.Text = one.InIcmp.ToString();
                label79.Text = one.InRip.ToString();

                label63.Text = one.OutAll.ToString();
                label30.Text = one.OutArp.ToString();
                label29.Text = one.OutTcp.ToString();
                label28.Text = one.OutUdp.ToString();
                label27.Text = one.OutIp.ToString();
                label26.Text = one.OutIcmp.ToString();
                label80.Text = one.OutRip.ToString();

                label64.Text = two.InAll.ToString();
                label50.Text = two.InArp.ToString();
                label49.Text = two.InTcp.ToString();
                label48.Text = two.InUdp.ToString();
                label47.Text = two.InIp.ToString();
                label46.Text = two.InIcmp.ToString();
                label82.Text = two.InRip.ToString();

                label65.Text = two.OutAll.ToString();
                label40.Text = two.OutArp.ToString();
                label39.Text = two.OutTcp.ToString();
                label38.Text = two.OutUdp.ToString();
                label37.Text = two.OutIp.ToString();
                label36.Text = two.OutIcmp.ToString();
                label84.Text = two.OutRip.ToString();
            }));
        }

        private void button2_Click(object sender, EventArgs e)  // tlacidlo TEST ODOSLANIA Z 1
        {
            sendVia1(BuildArpPacketRequest(Device1, 1, destinationIP));
        }

        private void buttonSend_Click_1(object sender, EventArgs e) // tlacidlo TEST ODOSLANIA Z 2
        {
            sendVia2(BuildArpPacketRequest(Device2, 2, destinationIP));
        }

        private void setAddress(int rozhranie)
        {
            List<Row> riadky = new List<Row>();
            Row riadok = new Row();
            for (int i = routeTable.Count - 1; i >= 0; i--)
            {
                if ((routeTable[i].Type.Equals('c')) && routeTable[i].Inter.Equals(rozhranie))
                {
                    riadok.Ip = routeTable[i].Ip;
                    riadok.Mask = routeTable[i].Mask;
                    riadok.Next_hop = routeTable[i].Next_hop;
                    riadok.Metric = 15;
                    riadky.Add(riadok);
                    routeTable.RemoveAt(i);
                    break;
                }
            }

            destinationIP = dstIPtext.Text.ToString();
            if (rozhranie == 1)
            {
                sourceIP = srcIPtext.Text.ToString();
                int mask1 = Int32.Parse(numericUpDown1.Value.ToString());
                riadok = new Row();
                riadok.Ip = getNetwork(new IpV4Address(sourceIP), mask1);
                riadok.Mask = mask1;
                riadok.Next_hop = new IpV4Address("0.0.0.0");
                riadky.Add(riadok);
                sendVia2(BuildRipUpdate(Device2, 1, riadky));
                addRoute('c', new IpV4Address(sourceIP), mask1, 1);
                drawRouteTable();
            }
            else
            {
                source2IP = srcIPtext2.Text.ToString();
                int mask2 = Int32.Parse(numericUpDown2.Value.ToString());
                riadok = new Row();
                riadok.Ip = getNetwork(new IpV4Address(source2IP), mask2);
                riadok.Mask = mask2;
                riadok.Next_hop = new IpV4Address("0.0.0.0");
                riadky.Add(riadok);
                sendVia1(BuildRipUpdate(Device1, 2, riadky));
                addRoute('c', new IpV4Address(source2IP), mask2, 2);
                drawRouteTable();
            }
        }


        private IpV4Address getNetwork(IpV4Address ip, int mask)
        {
            IpV4Address actualIP = ip;

            string[] w = actualIP.ToString().Split('.');
            string r1 = Convert.ToString(Int32.Parse(w[0]), 2).PadLeft(8, '0');
            string r2 = Convert.ToString(Int32.Parse(w[1]), 2).PadLeft(8, '0');
            string r3 = Convert.ToString(Int32.Parse(w[2]), 2).PadLeft(8, '0');
            string r4 = Convert.ToString(Int32.Parse(w[3]), 2).PadLeft(8, '0');
            string actString = r1 + "" + r2 + "" + r3 + "" + r4;

            string actSub = actString.Substring(0, mask).PadRight(32, '0');

            int[] IPcka = { 0, 0, 0, 0 };
            for (int o = 0; o < 4; o++)
            {
                if (actSub.Length > 0)
                {
                    string sub = actSub.Substring(0, 8);
                    IPcka[o] = Convert.ToInt32(sub, 2);
                    actSub = actSub.Substring(8);
                }
                else
                {
                    break;
                }
            }
            IpV4Address siet = new IpV4Address(IPcka[0] + "." + IPcka[1] + "." + IPcka[2] + "." + IPcka[3]);
            return siet;
        }

        private void button5_Click(object sender, EventArgs e)  // tlacidlo ENABLE/DISABLE STATISTICS
        {
            if (button5.Text == "ON")
            {
                statistics = true;
                button5.Text = "OFF";
            }
            else if (button5.Text == "OFF")
            {
                statistics = false;
                button5.Text = "ON";
            }
        }

        private void addRoute(char type, IpV4Address ip, int mask, int inter)
        {
            IpV4Address siet = getNetwork(ip, mask);

            Row newrow = new Row();
            if (type.Equals('c'))
                newrow.Distance1 = 0;
            if (type.Equals('s'))
                newrow.Distance1 = 1;
            if (type.Equals('d'))
                newrow.Distance1 = 120;

            newrow.Type = type;
            newrow.Ip = siet;
            newrow.Mask = mask;

            newrow.InvalidTime1 = 180;
            newrow.FlushTime1 = 240;
            newrow.HoltDownTime1 = 180;
            newrow.Inter = inter;

            foreach (Row i in routeTable)
            {
                if (type.Equals('d') && i.Ip.Equals(siet) && i.Mask.Equals(mask) && i.Type.Equals('d'))
                {   // ak sa route tabulke nachadza zaznam s rovnakou IP a maskou
                    i.InvalidTime1 = 180;
                    i.FlushTime1 = 240;
                    i.HoltDownTime1 = 180;
                    return;
                }
                if (i.Ip.Equals(siet) && i.Mask.Equals(mask) && newrow.Distance1 >= i.Distance1)
                {
                    return;
                }
            }
            routeTable.Add(newrow);
            drawRouteTable();
        }
        private void addRoute(char type, IpV4Address ip, int mask, IpV4Address next_hop, int metric, int incomingInterface)
        {
            IpV4Address siet = getNetwork(ip, mask);

            Row newrow = new Row();
            if (type.Equals('c'))
                newrow.Distance1 = 0;
            if (type.Equals('s'))
                newrow.Distance1 = 1;
            if (type.Equals('d'))
                newrow.Distance1 = 120;

            newrow.Type = type;
            newrow.Ip = siet;
            newrow.Mask = mask;
            newrow.Next_hop = next_hop;
            newrow.Metric = metric;
            newrow.Inter = incomingInterface;

            newrow.InvalidTime1 = 180;
            newrow.FlushTime1 = 240;
            newrow.HoltDownTime1 = 180;

            foreach (Row i in routeTable)
            {
                if (i.Ip.Equals(siet) && i.Mask.Equals(mask) && i.Type.Equals('d'))
                {   // ak sa route tabulke nachadza zaznam s rovnakou IP a maskou

                    if (metric == 16)
                    {
                        routeTable.Remove(i);               // vymaz z route tabulky
                        List<Row> rout = new List<Row>();   // vytvor novu routu s 16 metrikou
                        rout.Add(i);                        // pridaj ju do zoznamu rout ktore posielas
                        rout[0].Metric = 15;
                        if (incomingInterface == 1)
                            sendVia2(BuildRipUpdate(Device2, 1, rout));     // odosli opacnym rozhranim, ako ktorym si poision prijal
                        if (incomingInterface == 2)
                            sendVia1(BuildRipUpdate(Device1, 2, rout));
                        return;
                    }
                    i.InvalidTime1 = 180;
                    i.FlushTime1 = 240;
                    i.HoltDownTime1 = 180;
                    if(i.Distance1.Equals(newrow.Distance1))
                        return;
                }
                /*
                if (i.Ip.Equals(siet) && i.Mask.Equals(mask) && newrow.Distance1 > i.Distance1)
                {
                    return;
                }
                */
                if (i.Ip.Equals(siet) && i.Mask.Equals(mask) && i.Distance1.Equals(120) && newrow.Distance1.Equals(120))
                {
                    if (i.Metric < newrow.Metric)
                    {

                        return;
                    }
                }
            }
            if (metric != 16)
            {
                routeTable.Add(newrow);
                drawRouteTable();
            }
        }

        private void addStatic(char type, IpV4Address ip, int mask, int interf, IpV4Address next_hop)
        {
            IpV4Address siet = getNetwork(ip, mask);

            Row newrow = new Row();
            if (type.Equals('c'))
                newrow.Distance1 = 0;
            if (type.Equals('s'))
                newrow.Distance1 = 1;
            if (type.Equals('d'))
                newrow.Distance1 = 120;
            newrow.Type = type;
            newrow.Ip = siet;
            newrow.Mask = mask;
            newrow.Next_hop = next_hop;
            newrow.Inter = interf;

            foreach (Row i in routeTable)
            {
                if (i.Ip.Equals(siet) && i.Mask.Equals(mask))
                {
                    if (newrow.Distance1 >= i.Distance1)
                    {
                        return;
                    }
                }
            }

            routeTable.Add(newrow);
            drawRouteTable();
        }


        private void button6_Click(object sender, EventArgs e)
        {   // pridavanie static routy pomocou interface
            IpV4Address ip = new IpV4Address(routeIP.Text);
            int mask = int.Parse(routeMask.Value.ToString());
            int inter = int.Parse(routeInterface.Value.ToString());

            addRoute('s', ip, mask, inter);

        }

        private void button7_Click(object sender, EventArgs e)
        {   // pridavanie static routy pomocou next-hop
            IpV4Address ip = new IpV4Address(routeIP.Text);
            int mask = int.Parse(routeMask.Value.ToString());
            IpV4Address nextHop = new IpV4Address(routeNextHop.Text);

            addRoute('s', ip, mask, nextHop, 0, 0);
        }

        private void button8_Click(object sender, EventArgs e)
        {   // RESET route Tabulky
            routeTable = new List<Row>();
        }

        private void button9_Click(object sender, EventArgs e)
        {   // Vymazanie zaznamu z route tabulk
            IpV4Address toDelete = new IpV4Address(textBox1.Text.ToString());
            foreach (Row r in routeTable)
            {
                if (r.Ip.Equals(toDelete) && r.Type.Equals('s'))
                {
                    routeTable.Remove(r);
                    return;
                }
                    

            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            setAddress(2);
            sendVia2(builtGratiousArp(Device2, 2));
        }


        private void button3_Click(object sender, EventArgs e)  // Tlacidlo RESET STATISTIKY
        {
            one = new Status();
            two = new Status();
            prijate1 = 0;
            prijate2 = 0;
            odoslane1 = 0;
            odoslane2 = 0;
        }

        private void ButtonPing_Click(object sender, EventArgs e)
        {
            Thread t1 = new Thread(new ThreadStart(this.SendPing));
            t1.IsBackground = true;
            t1.Start();
        }


        private void button10_Click(object sender, EventArgs e)
        {
            if (RIP1enable)
            {
                RIP1enable = false;
                button10.Text = "RIP Int 1: OFF";
            }
            else
            {
                RIP1enable = true;
                button10.Text = "RIP Int 1: ON";
                sendVia1(BuildRipRequest(Device1, 2));
            }
        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (RIP2enable)
            {
                RIP2enable = false;
                button11.Text = "RIP Int 2: OFF";
            }
            else
            {
                RIP2enable = true;
                button11.Text = "RIP Int 2: ON";
                sendVia2(BuildRipRequest(Device2, 1));
            }
        }

        private void sendVia1(Packet pp)
        {
            if (statistics)
            {
                odoslane1++;
                updateStatsOut(1, pp);
            }
            communicator.SendPacket(pp);
        }

        private void button13_Click(object sender, EventArgs e)
        {
            setAddress(1);
            sendVia1(builtGratiousArp(Device1, 1));
        }



        private void sendVia2(Packet pp2)
        {
            if (statistics)
            {
                odoslane2++;
                updateStatsOut(2, pp2);
            }

            communicator2.SendPacket(pp2);

        }

        private void button14_Click(object sender, EventArgs e)
        {
            if (button14.Text.Equals("ON"))
            {
                capturing1 = true;
                button14.Text = "OFF";
            }
            else
            {
                capturing1 = false;
                button14.Text = "ON";

                List<Row> route = new List<Row>();
                foreach (Row r in routeTable)
                {
                    if (r.Type.Equals('c') && r.Inter.Equals(1))
                    {
                        r.Metric = 15;
                        route.Add(r);
                        routeTable.Remove(r);
                        break;
                    }
                }
                if (RIP2enable)
                    sendVia2(BuildRipUpdate(Device2, 1, route));
            }
        }

        private void button15_Click(object sender, EventArgs e)
        {
            if (button15.Text.Equals("ON"))
            {
                capturing2 = true;
                button15.Text = "OFF";
            }
            else
            {
                capturing2 = false;
                button15.Text = "ON";

                List<Row> route = new List<Row>();
                foreach (Row r in routeTable)
                {
                    if (r.Type.Equals('c') && r.Inter.Equals(2))
                    {
                        r.Metric = 15;
                        route.Add(r);
                        routeTable.Remove(r);
                        break;
                    }
                }
                if (RIP1enable)
                    sendVia1(BuildRipUpdate(Device1, 2, route));
            }
        }

        private void button16_Click(object sender, EventArgs e)
        {
            IpV4Address ip = new IpV4Address(routeIP.Text);
            int mask = int.Parse(routeMask.Value.ToString());
            int inter = int.Parse(routeInterface.Value.ToString());
            IpV4Address nextHop = new IpV4Address(routeNextHop.Text);

            addStatic('s', ip, mask, inter, nextHop);
        }
        private static Packet BuildIcmpPacket(int numberOfPing, int i, ArpRow arp, IpV4Address dstIp, MacAddress srcMac)
        {
            IpV4Address srcIp;
            if (i == 1)
            {
                srcIp = new IpV4Address(sourceIP);
            }
            else
            {
                srcIp = new IpV4Address(source2IP);
            }

            EthernetLayer ethernetLayer =
                new EthernetLayer
                {
                    Source = srcMac,
                    Destination = arp.MAC1,
                    EtherType = EthernetType.None, // Will be filled automatically.
                };
            Random rnd = new Random();

            ushort identifier = (ushort)rnd.Next(0, 65535);
            ushort SequenceNumber = SEQ++;

            IpV4Layer ipV4Layer =
                new IpV4Layer
                {
                    Source = srcIp,
                    CurrentDestination = dstIp,
                    Fragmentation = IpV4Fragmentation.None,
                    HeaderChecksum = null, // Will be filled automatically.
                    Identification = 123,
                    Options = IpV4Options.None,
                    Protocol = null, // Will be filled automatically.
                    Ttl = 100,
                    TypeOfService = 8,
                };

            IcmpEchoLayer icmpLayer =
                new IcmpEchoLayer
                {
                    Checksum = null, // Will be filled automatically.
                    Identifier = identifier,
                    SequenceNumber = SequenceNumber,
                };
            if (i == 1)
            {
                interface1Pings[numberOfPing].Identifier = identifier;
                interface1Pings[numberOfPing].SequenceNumber1 = SequenceNumber;
            }
            else
            {
                interface2Pings[numberOfPing].Identifier = identifier;
                interface2Pings[numberOfPing].SequenceNumber1 = SequenceNumber;
            }

            PacketBuilder builder = new PacketBuilder(ethernetLayer, ipV4Layer, icmpLayer);

            return builder.Build(DateTime.Now);
        }

        // zariadenie, ktorym odosle / IP na ktoru sa pyta 
        private static Packet BuildArpPacketReply(LivePacketDevice dev, MacAddress targetMAC, IpV4Address targetIP, int device)          // ARP Reply
        {
            MacAddress myMac = dev.GetMacAddress();
            String actualIP = "0.0.0.0";

            byte[] sourceMacByte = myMac.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            if (device == 1)
                actualIP = sourceIP;

            if (device == 2)
                actualIP = source2IP;

            byte[] sourceIPByte = actualIP.Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();

            byte[] destinationMacByte = targetMAC.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            byte[] destinationIPByte = targetIP.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();     // aku ma dlzku???

            EthernetLayer ethernetLayer = new EthernetLayer
            {
                Source = myMac,
                Destination = targetMAC,
                EtherType = EthernetType.Arp, // Will be filled automatically.
            };

            ArpLayer arpLayer = new ArpLayer
            {
                ProtocolType = EthernetType.IpV4,
                Operation = ArpOperation.Reply,
                SenderHardwareAddress = sourceMacByte.AsReadOnly(), // 03:03:03:03:03:03.
                SenderProtocolAddress = sourceIPByte.AsReadOnly(), // 1.2.3.4.
                TargetHardwareAddress = destinationMacByte.AsReadOnly(), // 04:04:04:04:04:04.
                TargetProtocolAddress = destinationIPByte.AsReadOnly(), // 11.22.33.44 // na TUTO sa pytam
            };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, arpLayer);
            return builder.Build(DateTime.Now);
        }

        private static Packet BuildArpProxyReply(LivePacketDevice dev, IpV4Address srcIp, MacAddress targetMAC, IpV4Address targetIP, int device)          // ARP Reply
        {
            String actualIP = "0.0.0.0";
            MacAddress myMac = dev.GetMacAddress(); ;

            if (device == 1)
            {
                actualIP = srcIp.ToString();
            }


            if (device == 2)
            {
                actualIP = srcIp.ToString();
            }



            byte[] sourceMacByte = myMac.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            byte[] sourceIPByte = actualIP.Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();

            byte[] destinationMacByte = targetMAC.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            byte[] destinationIPByte = targetIP.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();     // aku ma dlzku???

            EthernetLayer ethernetLayer = new EthernetLayer
            {
                Source = myMac,
                Destination = targetMAC,
                EtherType = EthernetType.Arp, // Will be filled automatically.
            };

            ArpLayer arpLayer = new ArpLayer
            {
                ProtocolType = EthernetType.IpV4,
                Operation = ArpOperation.Reply,
                SenderHardwareAddress = sourceMacByte.AsReadOnly(), // 03:03:03:03:03:03.
                SenderProtocolAddress = sourceIPByte.AsReadOnly(), // 1.2.3.4.
                TargetHardwareAddress = destinationMacByte.AsReadOnly(), // 04:04:04:04:04:04.
                TargetProtocolAddress = destinationIPByte.AsReadOnly(), // 11.22.33.44 // na TUTO sa pytam
            };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, arpLayer);
            return builder.Build(DateTime.Now);
        }


        private static Packet BuildArpPacketRequest(LivePacketDevice dev, int device, String destIP)          // ARP request
        {
            MacAddress myMac = dev.GetMacAddress();
            byte[] sourceMacByte = myMac.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            String actualIP = "0.0.0.0";

            if (device == 1)
                actualIP = sourceIP;

            if (device == 2)
                actualIP = source2IP;

            byte[] sourceIPByte = actualIP.Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();
            byte[] destinationIPByte = destIP.Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();

            EthernetLayer ethernetLayer = new EthernetLayer
            {
                Source = new MacAddress(myMac.ToString()),
                Destination = new MacAddress("FF:FF:FF:FF:FF:FF"),
                EtherType = EthernetType.Arp, // Will be filled automatically.
            };

            ArpLayer arpLayer = new ArpLayer
            {
                ProtocolType = EthernetType.IpV4,
                Operation = ArpOperation.Request,
                SenderHardwareAddress = sourceMacByte.AsReadOnly(), // 03:03:03:03:03:03.
                SenderProtocolAddress = sourceIPByte.AsReadOnly(), // 1.2.3.4.
                TargetHardwareAddress = new byte[] { 0, 0, 0, 0, 0, 0 }.AsReadOnly(), // 04:04:04:04:04:04.
                TargetProtocolAddress = destinationIPByte.AsReadOnly(), // 11.22.33.44 // na TUTO sa pytam
            };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, arpLayer);
            return builder.Build(DateTime.Now);
        }

        private static Packet builtGratiousArp(LivePacketDevice dev, int device)
        {
            MacAddress myMac = dev.GetMacAddress();
            byte[] sourceMacByte = myMac.ToString().Split(':').Select(x => Convert.ToByte(x, 16)).ToArray();
            String actualIP = "0.0.0.0";

            if (device == 1)
                actualIP = sourceIP;

            if (device == 2)
                actualIP = source2IP;

            byte[] sourceIPByte = actualIP.Split('.').Select(x => Convert.ToByte(x, 10)).ToArray();

            EthernetLayer ethernetLayer = new EthernetLayer
            {
                Source = new MacAddress(myMac.ToString()),
                Destination = new MacAddress("FF:FF:FF:FF:FF:FF"),
                EtherType = EthernetType.Arp, // Will be filled automatically.
            };

            ArpLayer arpLayer = new ArpLayer
            {
                ProtocolType = EthernetType.IpV4,
                Operation = ArpOperation.Request,
                SenderHardwareAddress = sourceMacByte.AsReadOnly(),
                SenderProtocolAddress = sourceIPByte.AsReadOnly(),
                TargetHardwareAddress = new byte[] { 0, 0, 0, 0, 0, 0 }.AsReadOnly(),
                TargetProtocolAddress = sourceIPByte.AsReadOnly(),
            };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, arpLayer);
            return builder.Build(DateTime.Now);
        }



        private Packet BuildRipUpdate(LivePacketDevice dev, int v, List<Row> routy)
        {

            MacAddress myMac = dev.GetMacAddress();
            string actualIP = "0.0.0.0";
            if (v == 2)
                actualIP = sourceIP;

            if (v == 1)
                actualIP = source2IP;



            EthernetLayer ethernetLayer =
                new EthernetLayer
                {
                    Source = new MacAddress(dev.GetMacAddress().ToString()),
                    Destination = new MacAddress("01:00:5e:00:00:09"),
                    EtherType = EthernetType.None, // Will be filled automatically.
                };

            IpV4Layer ipV4Layer =
                new IpV4Layer
                {
                    Source = new IpV4Address(actualIP),
                    CurrentDestination = new IpV4Address("224.0.0.9"),
                    Fragmentation = IpV4Fragmentation.None,
                    HeaderChecksum = null, // Will be filled automatically.
                    Identification = 123,
                    Options = IpV4Options.None,
                    Protocol = null, // Will be filled automatically.
                    Ttl = 1,
                    TypeOfService = 0,
                };

            UdpLayer udpLayer =
                new UdpLayer
                {
                    SourcePort = 520,
                    DestinationPort = 520,
                    Checksum = null, // Will be filled automatically.
                    CalculateChecksumValue = true,
                };



            String hexastring = "";
            String uHeader = ByteArrayToString(new byte[] { 2, 2, 0, 0 });
            hexastring += uHeader;

            foreach (Row actual in routy)
            {
                String uFamily = ByteArrayToString(new byte[] { 0, 2 });
                String uTag = ByteArrayToString(new byte[] { 0, 0 });
                String uIP = ByteArrayToString(actual.Ip.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray());
                IpV4Address maska = new IpV4Address(GetSubnetMask((byte)actual.Mask));
                String uMask = ByteArrayToString(maska.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray());
                String uNexthop = ByteArrayToString(actual.Next_hop.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray());
                int mt = actual.Metric;
                mt++;
                String uMetric = mt.ToString("X").PadLeft(8, '0');
                hexastring += uFamily + uTag + uIP + uMask + uNexthop + uMetric;
            }
            PayloadLayer payloadLayer =
                new PayloadLayer
                {
                    Data = new Datagram(StringToByteArray(hexastring)),
                };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, ipV4Layer, udpLayer, payloadLayer);

            return builder.Build(DateTime.Now);

        }

        private Packet BuildRipRequest(LivePacketDevice dev, int v)
        {
            MacAddress myMac = dev.GetMacAddress();
            string actualIP = "0.0.0.0";
            if (v == 2)
                actualIP = sourceIP;

            if (v == 1)
                actualIP = source2IP;

            EthernetLayer ethernetLayer =
                new EthernetLayer
                {
                    Source = new MacAddress(dev.GetMacAddress().ToString()),
                    Destination = new MacAddress("01:00:5e:00:00:09"),
                    EtherType = EthernetType.None, // Will be filled automatically.
                };

            IpV4Layer ipV4Layer =
                new IpV4Layer
                {
                    Source = new IpV4Address(actualIP),
                    CurrentDestination = new IpV4Address("224.0.0.9"),
                    Fragmentation = IpV4Fragmentation.None,
                    HeaderChecksum = null, // Will be filled automatically.
                    Identification = 123,
                    Options = IpV4Options.None,
                    Protocol = null, // Will be filled automatically.
                    Ttl = 1,
                    TypeOfService = 0,
                };

            UdpLayer udpLayer =
                new UdpLayer
                {
                    SourcePort = 520,
                    DestinationPort = 520,
                    Checksum = null, // Will be filled automatically.
                    CalculateChecksumValue = true,
                };

            String hexastring = "";
            String uHeader = ByteArrayToString(new byte[] { 1, 2, 0, 0 });
            hexastring += uHeader;

            IpV4Address nullIP = new IpV4Address("0.0.0.0");
            Row r = new Row();
            r.Ip = nullIP;
            r.Next_hop = nullIP;
            List<Row> routy = new List<Row>();
            routy.Add(r);
            foreach (Row actual in routy)
            {
                String uFamily = ByteArrayToString(new byte[] { 0, 0 });
                String uTag = ByteArrayToString(new byte[] { 0, 0 });
                String uIP = ByteArrayToString(actual.Ip.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray());
                IpV4Address maska = nullIP;
                String uMask = ByteArrayToString(maska.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray());
                String uNexthop = ByteArrayToString(actual.Next_hop.ToString().Split('.').Select(x => Convert.ToByte(x, 10)).ToArray());
                int mt = 16;
                String uMetric = mt.ToString("X").PadLeft(8, '0');
                hexastring += uFamily + uTag + uIP + uMask + uNexthop + uMetric;
            }

            PayloadLayer payloadLayer =
                new PayloadLayer
                {
                    Data = new Datagram(StringToByteArray(hexastring)),
                };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, ipV4Layer, udpLayer, payloadLayer);

            return builder.Build(DateTime.Now);

        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
        public string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private void RIPAnswer(int x)   // x - pre ktore rozhranie hladame routy .... Odosleme opacnym
        {
            List<Row> route1 = new List<Row>();

            foreach (Row r in routeTable)
            {
                if (r.Type.Equals('d') && r.Inter.Equals(x)) { route1.Add(r); }

                if (r.Type.Equals('c') && r.Inter.Equals(x))
                {
                    Row y = new Row();
                    y = r;
                    y.Metric = 1;
                    y.Next_hop = new IpV4Address("0.0.0.0");
                    route1.Add(r);
                }

                if (x == 2 && RIP1enable && route1.Count.Equals(25))
                {
                    sendVia1(BuildRipUpdate(Device1, 2, route1));
                    route1.Clear();
                }
                if (x == 1 && RIP2enable && route1.Count.Equals(25))
                {
                    sendVia2(BuildRipUpdate(Device2, 1, route1));
                    route1.Clear();
                }
            }
            if (x == 2 && RIP1enable && (route1.Count > 0))
            {
                sendVia1(BuildRipUpdate(Device1, 2, route1));
            }
            if (x == 1 && RIP2enable && (route1.Count > 0))
            {
                sendVia2(BuildRipUpdate(Device2, 1, route1));
            }
        }
    }
}

