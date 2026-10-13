using System.Net.Sockets;
using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Streams the scan to a Mana editor over Wi-Fi (ScanStream over TCP). Everything is queued and sent from a thread
/// of its own, so ARKit's queue never waits on the network: a piece changed twice before it goes out is sent once,
/// as it is now; photos wait their turn (the oldest dropped if the network falls behind); only the latest pose
/// matters.
/// </summary>
public sealed class ScanStreamClient : IDisposable
{
    private const int MaxQueuedPhotos = 12;

    private readonly string _host;
    private readonly int _port;
    private readonly string _device;
    private readonly Func<int, int, byte[], byte[]> _encode;
    private readonly object _lock = new();
    // A null value is a removal.
    private readonly Dictionary<Guid, ScanMeshPiece?> _pieces = new();
    private readonly Queue<ScanKeyframe> _photos = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private (Matrix4x4 Pose, double Time)? _pose;
    private bool _clear;
    private long _bytes;

    public ScanStreamClient(string name, string host, int port, string device, Func<int, int, byte[], byte[]> encode)
    {
        Name = name;
        _host = host;
        _port = port;
        _device = device;
        _encode = encode;
        new Thread(Run) { IsBackground = true, Name = "ManaLidar stream" }.Start();
    }

    /// <summary>The editor, as it announced itself (or the address typed in).</summary>
    public string Name { get; }
    public bool Connected { get; private set; }
    /// <summary>Why streaming stopped, once it has.</summary>
    public string? Failure { get; private set; }
    public long BytesSent => Interlocked.Read(ref _bytes);

    public void SendPiece(ScanMeshPiece piece) => Queue(() => _pieces[piece.Id] = piece);

    public void SendRemove(Guid id) => Queue(() => _pieces[id] = null);

    public void SendPose(Matrix4x4 cameraToWorld, double time) => Queue(() => _pose = (cameraToWorld, time));

    public void SendKeyframe(ScanKeyframe keyframe) => Queue(() =>
    {
        _photos.Enqueue(keyframe);
        while (_photos.Count > MaxQueuedPhotos)
        {
            _photos.Dequeue();
        }
    });

    /// <summary>A new scan: the editor forgets the old one, and anything not yet sent of it is dropped.</summary>
    public void SendClear() => Queue(() =>
    {
        _clear = true;
        _pieces.Clear();
        _photos.Clear();
    });

    /// <summary>Everything scanned so far, for an editor joining part-way through.</summary>
    public void SendAll(IEnumerable<ScanMeshPiece> pieces, IEnumerable<ScanKeyframe> keyframes) => Queue(() =>
    {
        foreach (var piece in pieces)
        {
            _pieces[piece.Id] = piece;
        }
        foreach (var keyframe in keyframes)
        {
            _photos.Enqueue(keyframe);
        }
    });

    public void Dispose()
    {
        _stop.Cancel();
        _wake.Set();
    }

    private void Queue(Action change)
    {
        if (Failure != null)
        {
            return;
        }
        lock (_lock)
        {
            change();
        }
        _wake.Set();
    }

    private void Run()
    {
        try
        {
            using var client = new TcpClient { NoDelay = true };
            if (!client.ConnectAsync(_host, _port).Wait(TimeSpan.FromSeconds(6)))
            {
                throw new TimeoutException($"No answer from {_host}:{_port}. Is Live Scan receiving on the PC?");
            }
            using var stream = new CountingStream(client.GetStream(), count => Interlocked.Add(ref _bytes, count));
            ScanStream.Write(stream, new ScanHello(_device, ScanStream.Version));
            Connected = true;

            while (!_stop.IsCancellationRequested)
            {
                _wake.WaitOne(100);
                bool clear;
                (Guid Id, ScanMeshPiece? Piece)[] pieces;
                ScanKeyframe? photo;
                (Matrix4x4 Pose, double Time)? pose;
                lock (_lock)
                {
                    clear = _clear;
                    _clear = false;
                    pieces = _pieces.Select(p => (p.Key, p.Value)).ToArray();
                    _pieces.Clear();
                    // A photo a round, so mesh and pose keep flowing between them.
                    photo = _photos.Count > 0 ? _photos.Dequeue() : null;
                    pose = _pose;
                    _pose = null;
                }

                if (clear)
                {
                    ScanStream.Write(stream, new ScanClearMessage());
                }
                foreach (var (id, piece) in pieces)
                {
                    ScanStream.Write(stream, piece != null ? new ScanPieceMessage(piece) : new ScanRemoveMessage(id));
                }
                if (photo != null)
                {
                    ScanStream.Write(stream, ScanKeyframeMessage.From(photo, _encode));
                    lock (_lock)
                    {
                        if (_photos.Count > 0)
                        {
                            _wake.Set();
                        }
                    }
                }
                if (pose is { } p)
                {
                    ScanStream.Write(stream, new ScanPoseMessage(p.Pose, p.Time));
                }
            }
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            Failure = ex is AggregateException { InnerException: { } inner } ? inner.Message : ex.Message;
        }
        catch (Exception)
        {
        }
        finally
        {
            Connected = false;
        }
    }

    // Counts what goes out, for the status line.
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<int> _counted;

        public CountingStream(Stream inner, Action<int> counted)
        {
            _inner = inner;
            _counted = counted;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            _counted(count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
            _counted(buffer.Length);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

/// <summary>Finds Mana editors on the local network that are receiving live scans (Bonjour, _manascan._tcp).</summary>
public static class ScanStreamBrowser
{
    /// <summary>An editor: its name, and the address to connect to (an IP when Bonjour gave one, else the host name).</summary>
    public sealed record Found(string Name, string Host, int Port);

    /// <summary>How many editors Bonjour saw on the last search, before resolving: tells "nothing answered" from
    /// "answered but could not be resolved".</summary>
    public static int LastBrowsed { get; private set; }

    /// <summary>The network the last sweep knocked on (e.g. "192.168.1.x"), or why there was none.</summary>
    public static string LastSwept { get; private set; } = "nothing";

    /// <summary>
    /// Bonjour and a sweep of this phone's own network at once, merged by address: Bonjour gives names, the sweep finds
    /// an editor even where Bonjour does not get through. Call on the main thread.
    /// </summary>
    public static async Task<IReadOnlyList<Found>> FindEverywhereAsync(double seconds = 3.0)
    {
        var bonjour = FindAsync(seconds);
        var sweep = SweepAsync(ScanStream.Port, TimeSpan.FromMilliseconds(600));
        var found = new List<Found>(await bonjour);
        foreach (var direct in await sweep)
        {
            if (!found.Any(f => f.Host == direct.Host))
            {
                found.Add(direct);
            }
        }
        return found;
    }

    /// <summary>
    /// Knocks on <paramref name="port"/> at every address of this phone's own network (a /24), all at once. A Mana
    /// editor that is receiving answers; the knock connects and leaves without a hello, which the editor ignores.
    /// </summary>
    public static async Task<IReadOnlyList<Found>> SweepAsync(int port, TimeSpan timeout)
    {
        // The Wi-Fi's address: en0 on an iPhone. Not the first one going -- cellular (pdp_ip0) and VPN tunnels
        // (utun, e.g. Tailscale) have IPv4 addresses too, and sweeping theirs reaches nothing at home.
        var candidates = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(a => a.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && a.Name.StartsWith("en"))
            .OrderBy(a => a.Name == "en0" ? 0 : 1)
            .SelectMany(a => a.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address)
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && IsHomeNetwork(a.GetAddressBytes()));
        var mine = candidates.FirstOrDefault();
        if (mine == null)
        {
            LastSwept = "no Wi-Fi address";
            return Array.Empty<Found>();
        }
        var prefix = mine.GetAddressBytes();
        LastSwept = $"{prefix[0]}.{prefix[1]}.{prefix[2]}.x";
        var knocks = Enumerable.Range(1, 254).Where(last => last != prefix[3]).Select(async last =>
        {
            var host = $"{prefix[0]}.{prefix[1]}.{prefix[2]}.{last}";
            using var client = new System.Net.Sockets.TcpClient();
            try
            {
                var connect = client.ConnectAsync(host, port);
                if (await Task.WhenAny(connect, Task.Delay(timeout)) == connect && client.Connected)
                {
                    return new Found($"Mana at {host}", host, port);
                }
            }
            catch (Exception)
            {
            }
            return null;
        });
        return (await Task.WhenAll(knocks)).OfType<Found>().ToArray();
    }

    // 10/8, 172.16/12 and 192.168/16: the addresses a home or office network hands out.
    private static bool IsHomeNetwork(byte[] b) =>
        b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);

    /// <summary>Looks for <paramref name="seconds"/>, then resolves what answered. Call on the main thread.</summary>
    public static async Task<IReadOnlyList<Found>> FindAsync(double seconds = 2.0)
    {
        var browser = new NSNetServiceBrowser();
        var services = new List<NSNetService>();
        browser.FoundService += (_, e) => services.Add(e.Service);
        browser.SearchForServices(ScanStream.Service + ".", "local.");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        browser.Stop();
        LastBrowsed = services.Count;

        var found = new List<Found>();
        foreach (var service in services)
        {
            var resolved = new TaskCompletionSource<bool>();
            service.AddressResolved += (_, _) => resolved.TrySetResult(true);
            service.ResolveFailure += (_, _) => resolved.TrySetResult(false);
            service.Resolve(3.0);
            if (await Task.WhenAny(resolved.Task, Task.Delay(3500)) == resolved.Task && resolved.Task.Result)
            {
                // The resolved IP, not the host name: .NET on iOS cannot look up a ".local" name ("address could not
                // be found"), while Bonjour has already found the address.
                var address = Ipv4(service) ?? service.HostName?.TrimEnd('.');
                if (!string.IsNullOrEmpty(address))
                {
                    found.Add(new Found(service.Name, address, (int)service.Port));
                }
            }
        }
        return found;
    }

    // The first IPv4 address among the service's resolved socket addresses (Darwin sockaddr_in: length, family 2,
    // port, then the four address bytes).
    private static string? Ipv4(NSNetService service)
    {
        foreach (var data in service.Addresses ?? Array.Empty<NSData>())
        {
            var bytes = data.ToArray();
            if (bytes.Length >= 8 && bytes[1] == 2)
            {
                return $"{bytes[4]}.{bytes[5]}.{bytes[6]}.{bytes[7]}";
            }
        }
        return null;
    }
}
