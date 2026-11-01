using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Sill.Windows.Net;

/// <summary>How a Mac's home door speaks, from its TXT record's `p` (Pairing.swift's HomeDoorTXT).</summary>
public enum Door
{
    /// <summary>No `p`: plain TCP, no pairing (development builds, SillHost without --pairing).</summary>
    Plain,
    /// <summary>`p=1` (or any value but 0): TLS, paired devices only.</summary>
    PairingRequired,
    /// <summary>`p=0`: TLS, any device (the Mac's Require pairing is off).</summary>
    Open,
}

/// <summary>A Mac running Sill, as Bonjour lists it.</summary>
public sealed class MacService
{
    public string FullName { get; init; } = "";          // "Mac mini._sill._tcp.local"
    public string Name { get; init; } = "";              // "Mac mini"
    public string? HostName { get; set; }
    public int Port { get; set; }
    public Dictionary<string, string?> Txt { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Where to dial, best first: addresses on the network the answer came by, IPv4 first.</summary>
    public List<IPAddress> Addresses { get; set; } = new();
    public DateTime LastSeen { get; set; }

    public Door Door => !Txt.TryGetValue("p", out var v) ? Door.Plain : v == "0" ? Door.Open : Door.PairingRequired;
    public string? Tag => Txt.TryGetValue("r", out var v) ? v : null;
    public bool Complete => Port > 0 && Addresses.Count > 0;
}

/// <summary>A Bonjour browser for `_sill._tcp`, without Bonjour: one-shot multicast DNS queries
/// (RFC 6762 §5.1) from an ephemeral port on every IPv4 interface that is up, so each Mac answers by
/// unicast to this socket alone and nothing here shares port 5353 with Windows' own responder.
/// Answers carry the SRV, TXT and addresses; whatever is missing is asked for by name. A Mac not
/// heard from for a while, or one that says goodbye (TTL 0), leaves the list.</summary>
public sealed class MdnsBrowser : IDisposable
{
    public const string ServiceType = "_sill._tcp.local";
    static readonly IPEndPoint Group = new(IPAddress.Parse("224.0.0.251"), 5353);
    static readonly TimeSpan Forget = TimeSpan.FromSeconds(25);

    readonly string serviceType;
    readonly object gate = new();
    readonly Dictionary<string, MacService> services = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (List<IPAddress> addrs, DateTime at)> hosts = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Socket socket, IPAddress local)> sockets = new();
    readonly CancellationTokenSource stop = new();
    DateTime started;

    /// <summary>The list changed: on a thread of the browser's, never the UI's.</summary>
    public event Action? Changed;

    public MdnsBrowser(string serviceType = ServiceType) { this.serviceType = serviceType; }

    public IReadOnlyList<MacService> Services
    {
        get { lock (gate) return services.Values.Where(s => s.Complete).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); }
    }

    public void Start()
    {
        started = DateTime.UtcNow;
        OpenSockets();
        _ = Task.Run(QueryLoop);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    void OnNetworkChanged(object? sender, EventArgs e)
    {
        lock (gate)
        {
            foreach (var (s, _) in sockets) s.Dispose();
            sockets.Clear();
        }
        OpenSockets();
        Query(serviceType, 12);
    }

    void OpenSockets()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || !ni.SupportsMulticast ||
                ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                try
                {
                    var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    s.Bind(new IPEndPoint(ua.Address, 0));
                    s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, BitConverter.ToInt32(ua.Address.GetAddressBytes(), 0));
                    s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                    lock (gate) sockets.Add((s, ua.Address));
                    _ = Task.Run(() => ReceiveLoop(s, ua.Address));
                }
                catch (SocketException) { }
            }
        }
    }

    async Task QueryLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            Query(serviceType, 12);
            Expire();
            var age = DateTime.UtcNow - started;
            try { await Task.Delay(age < TimeSpan.FromSeconds(10) ? 1500 : 6000, stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Looks again now (the user asked, or a dial failed).</summary>
    public void Refresh() => Query(serviceType, 12);

    void Expire()
    {
        bool changed = false;
        lock (gate)
        {
            var now = DateTime.UtcNow;
            foreach (var key in services.Where(kv => now - kv.Value.LastSeen > Forget).Select(kv => kv.Key).ToList())
            {
                services.Remove(key);
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    void Query(string name, ushort type, ushort type2 = 0)
    {
        var q = new List<byte>(64) { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        int count = 0;
        foreach (var t in new[] { type, type2 })
        {
            if (t == 0) continue;
            WriteName(q, name);
            q.Add((byte)(t >> 8)); q.Add((byte)t);
            q.Add(0); q.Add(1);                       // IN
            count++;
        }
        q[5] = (byte)count;
        var packet = q.ToArray();
        List<Socket> all;
        lock (gate) all = sockets.Select(s => s.socket).ToList();
        foreach (var s in all)
        {
            try { s.SendTo(packet, Group); } catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
        }
    }

    void WriteName(List<byte> b, string name)
    {
        // A service instance's own label may hold dots ("Mac.home"): it is everything before the
        // service type, one label.
        var labels = new List<string>();
        var suffix = "." + serviceType;
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
        {
            labels.Add(name[..^suffix.Length]);
            labels.AddRange(serviceType.Split('.'));
        }
        else labels.AddRange(name.Split('.', StringSplitOptions.RemoveEmptyEntries));
        foreach (var label in labels)
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            b.Add((byte)Math.Min(bytes.Length, 63));
            b.AddRange(bytes.Take(63));
        }
        b.Add(0);
    }

    async Task ReceiveLoop(Socket s, IPAddress local)
    {
        var buffer = new byte[9000];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!stop.IsCancellationRequested)
        {
            SocketReceiveFromResult r;
            try { r = await s.ReceiveFromAsync(buffer, SocketFlags.None, any, stop.Token); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException) { return; }
            try { Handle(buffer.AsSpan(0, r.ReceivedBytes).ToArray(), ((IPEndPoint)r.RemoteEndPoint).Address); }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException) { }
        }
    }

    void Handle(byte[] m, IPAddress from)
    {
        if (m.Length < 12 || (m[2] & 0x80) == 0) return;             // answers only
        int qd = m[4] << 8 | m[5], an = m[6] << 8 | m[7], ns = m[8] << 8 | m[9], ar = m[10] << 8 | m[11];
        int o = 12;
        for (int i = 0; i < qd; i++) { ReadName(m, ref o); o += 4; }
        var now = DateTime.UtcNow;
        bool changed = false;
        var wantDetails = new List<string>();
        var wantHosts = new List<string>();
        lock (gate)
        {
            for (int i = 0; i < an + ns + ar; i++)
            {
                var owner = ReadName(m, ref o);
                int type = m[o] << 8 | m[o + 1];
                uint ttl = (uint)(m[o + 4] << 24 | m[o + 5] << 16 | m[o + 6] << 8 | m[o + 7]);
                int len = m[o + 8] << 8 | m[o + 9];
                o += 10;
                int rd = o;
                o += len;
                if (o > m.Length) return;
                switch (type)
                {
                    case 12 when owner.Equals(serviceType, StringComparison.OrdinalIgnoreCase):
                    {
                        int p = rd;
                        var instance = ReadName(m, ref p);
                        if (ttl == 0) { changed |= services.Remove(instance); break; }
                        if (!services.TryGetValue(instance, out var svc))
                        {
                            svc = new MacService { FullName = instance, Name = InstanceLabel(instance) };
                            services[instance] = svc;
                            changed = true;
                        }
                        svc.LastSeen = now;
                        if (!svc.Complete) wantDetails.Add(instance);
                        AddAddress(svc, from);
                        break;
                    }
                    case 33:
                    {
                        if (!services.TryGetValue(owner, out var svc)) break;
                        int p = rd + 4;
                        int port = m[p] << 8 | m[p + 1];
                        p += 2;
                        var target = ReadName(m, ref p);
                        if (svc.Port != port || svc.HostName != target) changed = true;
                        svc.Port = port; svc.HostName = target; svc.LastSeen = now;
                        AddAddress(svc, from);
                        if (hosts.TryGetValue(target, out var h)) foreach (var a in h.addrs) changed |= AddAddress(svc, a);
                        else wantHosts.Add(target);
                        break;
                    }
                    case 16:
                    {
                        if (!services.TryGetValue(owner, out var svc)) break;
                        var txt = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        int p = rd;
                        while (p < rd + len)
                        {
                            int l = m[p++];
                            if (l == 0 || p + l > rd + len) { p += l; continue; }
                            var entry = Encoding.UTF8.GetString(m, p, l);
                            p += l;
                            int eq = entry.IndexOf('=');
                            var key = eq < 0 ? entry : entry[..eq];
                            if (!txt.ContainsKey(key)) txt[key] = eq < 0 ? null : entry[(eq + 1)..];
                        }
                        if (!txt.OrderBy(k => k.Key).SequenceEqual(svc.Txt.OrderBy(k => k.Key))) changed = true;
                        svc.Txt = txt;
                        break;
                    }
                    case 1 when len == 4:
                    case 28 when len == 16:
                    {
                        var addr = new IPAddress(m.AsSpan(rd, len));
                        if (addr.IsIPv6LinkLocal) break;          // no scope here to dial it by
                        if (!hosts.TryGetValue(owner, out var h) || now - h.at > Forget) h = (new List<IPAddress>(), now);
                        if (!h.addrs.Contains(addr)) h.addrs.Add(addr);
                        hosts[owner] = (h.addrs, now);
                        foreach (var svc in services.Values.Where(x => string.Equals(x.HostName, owner, StringComparison.OrdinalIgnoreCase)))
                            changed |= AddAddress(svc, addr);
                        break;
                    }
                }
            }
        }
        foreach (var d in wantDetails.Distinct()) Query(d, 33, 16);
        foreach (var h in wantHosts.Distinct()) Query(h, 1);
        if (changed) Changed?.Invoke();
    }

    static bool AddAddress(MacService svc, IPAddress a)
    {
        if (svc.Addresses.Contains(a)) return false;
        // IPv4 first: the Mac's answer came from it, so it is on the network that reaches this PC.
        if (a.AddressFamily == AddressFamily.InterNetwork) svc.Addresses.Insert(svc.Addresses.Count(x => x.AddressFamily == AddressFamily.InterNetwork), a);
        else svc.Addresses.Add(a);
        return true;
    }

    string InstanceLabel(string fullName)
    {
        var suffix = "." + serviceType;
        return fullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? fullName[..^suffix.Length] : fullName;
    }

    static string ReadName(byte[] m, ref int o)
    {
        var labels = new List<string>();
        int p = o;
        bool jumped = false;
        for (int guard = 0; guard < 128; guard++)
        {
            int l = m[p];
            if (l == 0) { p++; break; }
            if ((l & 0xC0) == 0xC0)
            {
                int target = (l & 0x3F) << 8 | m[p + 1];
                if (!jumped) o = p + 2;
                jumped = true;
                p = target;
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(m, p + 1, l));
            p += 1 + l;
        }
        if (!jumped) o = p;
        return string.Join(".", labels);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        stop.Cancel();
        lock (gate)
        {
            foreach (var (s, _) in sockets) s.Dispose();
            sockets.Clear();
        }
    }
}
