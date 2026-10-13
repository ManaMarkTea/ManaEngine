#if !MANA_BROWSER
using System.Diagnostics;
using System.Runtime.InteropServices;
using ManaCore.Diagnostics;

namespace ManaVoxel.Code.LiveScan;

/// <summary>
/// Announces the editor's live-scan port over Bonjour (_manascan._tcp), so the phone finds it without anyone typing
/// an address. Windows has a responder built in (DnsServiceRegister, Windows 10 1809 on); macOS has dns-sd.
/// Best effort: if neither works, the phone can still connect by address, which the Live Scan panel shows.
/// </summary>
public sealed class LiveScanAdvertiser : IDisposable
{
    private Process? _dnsSd;
    private IntPtr _instance;
    // In unmanaged memory: Windows holds on to the request until registration completes and for deregistering.
    private IntPtr _request;
    private IntPtr _ip4;
    private RegisterComplete? _callback;
    private bool _registered;

    public LiveScanAdvertiser(string name, int port)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                RegisterWindows(name, port);
            }
            else if (OperatingSystem.IsMacOS())
            {
                // Lives as long as the process: registration ends when it is killed.
                _dnsSd = Process.Start(new ProcessStartInfo("dns-sd", $"-R \"{name}\" {ManaLidar.ScanStream.Service} local {port}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            Announced = true;
        }
        catch (Exception ex)
        {
            ManaLog.WriteDiagnostic($"[LiveScan] Could not announce over Bonjour ({ex.Message}); connect by address instead.");
        }
    }

    public bool Announced { get; private set; }

    public void Dispose()
    {
        if (_registered)
        {
            try
            {
                DnsServiceDeRegister(_request, IntPtr.Zero);
            }
            catch (Exception)
            {
            }
            _registered = false;
        }
        if (_instance != IntPtr.Zero)
        {
            DnsServiceFreeInstance(_instance);
            _instance = IntPtr.Zero;
        }
        if (_request != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_request);
            _request = IntPtr.Zero;
        }
        if (_ip4 != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ip4);
            _ip4 = IntPtr.Zero;
        }
        if (_dnsSd is { HasExited: false })
        {
            _dnsSd.Kill();
        }
        _dnsSd?.Dispose();
    }

    private void RegisterWindows(string name, int port)
    {
        var host = Environment.MachineName + ".local";
        // The address goes in the announcement itself, so a phone has it without looking the name up.
        if (LocalIpv4() is { } address)
        {
            _ip4 = Marshal.AllocHGlobal(4);
            Marshal.Copy(address.GetAddressBytes(), 0, _ip4, 4);
        }
        _instance = DnsServiceConstructInstance($"{name}.{ManaLidar.ScanStream.Service}.local", host, _ip4, IntPtr.Zero,
            (ushort)port, 0, 0, 0, IntPtr.Zero, IntPtr.Zero);
        if (_instance == IntPtr.Zero)
        {
            throw new InvalidOperationException("DnsServiceConstructInstance failed.");
        }
        // Kept in a field: Windows calls it later, and a collected delegate would crash the process.
        _callback = (status, _, instance) =>
        {
            if (status != 0)
            {
                ManaLog.WriteDiagnostic($"[LiveScan] Bonjour registration finished with status {status}.");
            }
            if (instance != IntPtr.Zero)
            {
                DnsServiceFreeInstance(instance);
            }
        };
        var request = new DnsServiceRegisterRequest
        {
            Version = 1,
            InterfaceIndex = 0,
            ServiceInstance = _instance,
            RegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(_callback),
            QueryContext = IntPtr.Zero,
            Credentials = IntPtr.Zero,
            UnicastEnabled = 0,
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceRegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);
        var status = DnsServiceRegister(_request, IntPtr.Zero);
        // 9506 is DNS_REQUEST_PENDING: accepted, finishing in the background.
        if (status != 9506 && status != 0)
        {
            throw new InvalidOperationException($"DnsServiceRegister returned {status}.");
        }
        _registered = true;
    }

    // The LAN address a phone can reach: an up, non-loopback adapter with a gateway (not a VPN's or a virtual switch's).
    private static System.Net.IPAddress? LocalIpv4()
    {
        var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(a => a.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && a.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
        foreach (var adapter in adapters.OrderByDescending(a => a.GetIPProperties().GatewayAddresses.Count > 0))
        {
            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !address.ToString().StartsWith("169.254."))
                {
                    return address;
                }
            }
        }
        return null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RegisterComplete(uint status, IntPtr queryContext, IntPtr instance);

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr ServiceInstance;
        public IntPtr RegisterCompletionCallback;
        public IntPtr QueryContext;
        public IntPtr Credentials;
        public int UnicastEnabled;
    }

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string serviceName, string hostName, IntPtr ip4, IntPtr ip6,
        ushort port, ushort priority, ushort weight, uint propertiesCount, IntPtr keys, IntPtr values);

    [DllImport("dnsapi.dll")]
    private static extern void DnsServiceFreeInstance(IntPtr instance);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);
}
#endif
