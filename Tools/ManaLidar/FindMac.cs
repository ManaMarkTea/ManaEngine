using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Finds Macs on the local network the way Visual Studio's Pair to Mac does: a Mac with Remote Login on announces
// "_sftp-ssh._tcp" over Bonjour (multicast DNS). One question to 224.0.0.251:5353, asking for unicast answers so
// nothing has to share the system's own port 5353, then whatever replies within the wait. Loaded by
// deploy-ios-from-windows.ps1 with Add-Type.
public static class ManaFindMac
{
    public sealed class Found
    {
        public string Name = "";
        public string Host = "";
        public string Address = "";
        public override string ToString() => $"{Name} ({Host}{(Address.Length > 0 ? ", " + Address : "")})";
    }

    public static List<Found> Find(int waitMilliseconds) => Find(waitMilliseconds, "_sftp-ssh._tcp");

    public static List<Found> Find(int waitMilliseconds, string service)
    {
        var found = new List<Found>();
        var query = BuildQuery(service + ".local");
        var group = IPAddress.Parse("224.0.0.251");
        var sockets = new List<UdpClient>();

        // Ask from every IPv4 address this PC has (Wi-Fi, Ethernet...): a multicast left to the default route can
        // go out a VPN or virtual adapter and reach no one. TTL 255, as multicast DNS expects of a query.
        foreach (var local in LocalAddresses())
        {
            try
            {
                var udp = new UdpClient(new IPEndPoint(local, 0));
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                udp.Send(query, query.Length, new IPEndPoint(group, 5353));
                sockets.Add(udp);
            }
            catch (SocketException)
            {
            }
        }

        // Some responders answer by multicast however they are asked, so listen on the shared port too.
        try
        {
            var shared = new UdpClient();
            shared.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            shared.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
            foreach (var local in LocalAddresses())
            {
                shared.JoinMulticastGroup(group, local);
            }
            sockets.Add(shared);
        }
        catch (SocketException)
        {
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(waitMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var udp in sockets)
            {
                while (udp.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    try
                    {
                        Read(udp.Receive(ref from), from, found, service);
                    }
                    catch (Exception)
                    {
                        // A garbled or unrelated packet: ignore it and keep listening.
                    }
                }
            }
            System.Threading.Thread.Sleep(20);
        }
        foreach (var udp in sockets)
        {
            udp.Dispose();
        }
        return found;
    }

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                || adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            {
                continue;
            }
            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork && !address.ToString().StartsWith("169.254."))
                {
                    yield return address;
                }
            }
        }
    }

    private static byte[] BuildQuery(string name)
    {
        var packet = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.Split('.'))
        {
            packet.Add((byte)label.Length);
            packet.AddRange(Encoding.ASCII.GetBytes(label));
        }
        packet.Add(0);
        packet.AddRange(new byte[] { 0, 12 });      // PTR
        packet.AddRange(new byte[] { 0x80, 1 });    // IN, with the "answer me directly" (QU) bit
        return packet.ToArray();
    }

    private static void Read(byte[] p, IPEndPoint from, List<Found> found, string service)
    {
        int at = 12;
        int questions = p[4] << 8 | p[5];
        int records = (p[6] << 8 | p[7]) + (p[8] << 8 | p[9]) + (p[10] << 8 | p[11]);
        for (int q = 0; q < questions; q++)
        {
            ReadName(p, ref at);
            at += 4;
        }

        string instance = "", host = "", address = "";
        var addresses = new Dictionary<string, string>();
        for (int r = 0; r < records; r++)
        {
            var name = ReadName(p, ref at);
            int type = p[at] << 8 | p[at + 1];
            int length = p[at + 8] << 8 | p[at + 9];
            int data = at + 10;
            if (type == 12 && name.StartsWith(service, StringComparison.OrdinalIgnoreCase))
            {
                int d = data;
                instance = ReadName(p, ref d);
            }
            else if (type == 33)
            {
                int d = data + 6;           // priority, weight, port
                host = ReadName(p, ref d);
            }
            else if (type == 1 && length == 4)
            {
                addresses[name] = $"{p[data]}.{p[data + 1]}.{p[data + 2]}.{p[data + 3]}";
            }
            at = data + length;
        }
        if (instance.Length == 0)
        {
            return;
        }
        if (host.Length > 0 && addresses.TryGetValue(host, out var a))
        {
            address = a;
        }
        else
        {
            // The reply came from the Mac itself, so its source address will do.
            address = from.Address.ToString();
        }
        var display = instance.Split(new[] { "." + service }, StringSplitOptions.None)[0];
        foreach (var existing in found)
        {
            if (existing.Name == display)
            {
                return;
            }
        }
        found.Add(new Found { Name = display, Host = host.TrimEnd('.'), Address = address });
    }

    private static string ReadName(byte[] p, ref int at)
    {
        var labels = new List<string>();
        int position = at;
        bool jumped = false;
        for (int guard = 0; guard < 64; guard++)
        {
            int length = p[position];
            if (length == 0)
            {
                position++;
                break;
            }
            if ((length & 0xC0) == 0xC0)
            {
                int pointer = (length & 0x3F) << 8 | p[position + 1];
                if (!jumped)
                {
                    at = position + 2;
                }
                jumped = true;
                position = pointer;
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(p, position + 1, length));
            position += length + 1;
        }
        if (!jumped)
        {
            at = position;
        }
        return string.Join(".", labels);
    }
}
