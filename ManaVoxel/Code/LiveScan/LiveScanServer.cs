#if !MANA_BROWSER
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ManaCore.Diagnostics;
using ManaLidar;

namespace ManaVoxel.Code.LiveScan;

/// <summary>
/// Listens for a phone streaming a scan (ScanStream over TCP), one phone at a time, and queues what it sends for
/// the editor to apply on its own thread. A newer connection replaces the old: a phone that dropped off Wi-Fi and
/// came back just carries on.
/// </summary>
public sealed class LiveScanServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly LiveScanAdvertiser _advertiser;
    private TcpClient? _client;

    public LiveScanServer()
    {
        // The first free port from the usual one, so two editors on one PC do not collide.
        TcpListener? listener = null;
        for (var port = ScanStream.Port; port < ScanStream.Port + 10 && listener == null; port++)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            catch (SocketException)
            {
                listener = null;
            }
        }
        _listener = listener ?? throw new InvalidOperationException($"No free port from {ScanStream.Port} to receive a live scan on.");
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _advertiser = new LiveScanAdvertiser($"Mana on {Environment.MachineName}", Port);
        _ = Task.Run(AcceptLoop);
    }

    public int Port { get; }

    /// <summary>Messages received, for the editor thread to apply.</summary>
    public ConcurrentQueue<ScanStreamMessage> Received { get; } = new();

    /// <summary>The phone streaming now, by the name it gave, or null.</summary>
    public string? Phone { get; private set; }

    public bool Announced => _advertiser.Announced;

    public long BytesReceived => Interlocked.Read(ref _bytes);
    private long _bytes;

    /// <summary>Where a phone can reach this editor, for typing in when Bonjour cannot find it.</summary>
    // Asked every frame by the panel; listing the adapters costs milliseconds, so it is re-read only now and then.
    public IReadOnlyList<string> Addresses
    {
        get
        {
            var now = Environment.TickCount64;
            if (_addresses == null || now - _addressesRead > 5000)
            {
                _addresses = ReadAddresses();
                _addressesRead = now;
            }
            return _addresses;
        }
    }

    private IReadOnlyList<string>? _addresses;
    private long _addressesRead;

    private IReadOnlyList<string> ReadAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(a => a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(a => a.GetIPProperties().UnicastAddresses)
        .Select(u => u.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
        .Select(a => $"{a}:{Port}")
        .ToArray();

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
            {
                return;
            }
            Interlocked.Exchange(ref _client, client)?.Dispose();
            _ = Task.Run(() => ReadLoop(client));
        }
    }

    private void ReadLoop(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            using var stream = new CountingStream(client.GetStream(), count => Interlocked.Add(ref _bytes, count));
            while (!_stop.IsCancellationRequested && ScanStream.Read(stream) is { } message)
            {
                if (message is ScanHello hello)
                {
                    Phone = hello.Device;
                    ManaLog.WriteDiagnostic($"[LiveScan] {hello.Device} connected.");
                }
                Received.Enqueue(message);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or SocketException)
        {
            ManaLog.WriteDiagnostic($"[LiveScan] Connection ended: {ex.Message}");
        }
        finally
        {
            // Only the current phone's leaving clears its name: an older connection closing must not.
            if (ReferenceEquals(Interlocked.CompareExchange(ref _client, null, client), client))
            {
                Phone = null;
            }
            client.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _client?.Dispose();
        _advertiser.Dispose();
    }

    // Counts what comes in, for the panel's throughput.
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<int> _counted;

        public CountingStream(Stream inner, Action<int> counted)
        {
            _inner = inner;
            _counted = counted;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            _counted(read);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
#endif
