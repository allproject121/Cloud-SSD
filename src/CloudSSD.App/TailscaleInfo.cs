using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace CloudSSD.App;

/// <summary>
/// What this PC looks like on Tailscale: its tailnet addresses and its MagicDNS name. Read from the network adapters
/// (and, for the name, from "tailscale status --json"); nothing is installed or changed. Cached for a minute.
/// </summary>
public static class TailscaleInfo
{
    public sealed record Snapshot(IReadOnlyList<string> Ips, string? DnsName)
    {
        public bool Available => Ips.Count > 0;
    }

    private static readonly object Gate = new();
    private static Snapshot? _cached;
    private static DateTime _cachedAt;

    /// <summary>Tests (and machines without Tailscale) can supply their own picture.</summary>
    internal static Func<Snapshot>? Override;

    /// <summary>100.64.0.0/10 (Tailscale's IPv4 range, the carrier-grade NAT block) or fd7a:115c:a1e0::/48 (its IPv6 range).</summary>
    public static bool IsTailnetAddress(IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork) return b[0] == 100 && (b[1] & 0xC0) == 64;
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && b[0] == 0xfd && b[1] == 0x7a && b[2] == 0x11 && b[3] == 0x5c && b[4] == 0xa1 && b[5] == 0xe0;
    }

    /// <summary>Drops the cached picture (Tailscale was just installed or signed in).</summary>
    public static void Forget()
    {
        lock (Gate) _cached = null;
    }

    public static Snapshot Current()
    {
        if (Override is { } o) return o();
        lock (Gate)
        {
            if (_cached != null && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(60)) return _cached;
        }
        var snap = new Snapshot(LocalTailnetIps(), _cached?.DnsName ?? ReadDnsName());
        lock (Gate)
        {
            _cached = snap;
            _cachedAt = DateTime.UtcNow;
        }
        return snap;
    }

    private static List<string> LocalTailnetIps()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (IsTailnetAddress(ua.Address)) list.Add(ua.Address.ToString());
            }
        }
        catch (NetworkInformationException) { }
        // IPv4 first: it is what people type and what the phone tries first.
        return list.OrderBy(a => a.Contains(':') ? 1 : 0).ToList();
    }

    /// <summary>The MagicDNS name ("roshan.tail1234.ts.net"), or null when Tailscale is not installed or not running.</summary>
    private static string? ReadDnsName()
    {
        var exe = TailscaleSetup.FindExe();
        if (exe == null) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe)
            {
                ArgumentList = { "status", "--json" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000) || !output.Wait(500))
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
                return null;
            }
            using var doc = JsonDocument.Parse(output.Result);
            if (doc.RootElement.TryGetProperty("Self", out var self) && self.TryGetProperty("DNSName", out var dns) && dns.GetString() is { Length: > 0 } name)
                return name.TrimEnd('.');
        }
        catch (Exception e) when (e is IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        return null;
    }
}
