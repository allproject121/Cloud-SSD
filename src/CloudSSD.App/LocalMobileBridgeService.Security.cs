using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// Who may use the bridge and what they may reach: one token per phone (the PC keeps only its hash), the old shared
/// token for phones paired before that, signed short-lived file links that can be given to other apps, a switch for
/// every route, and file access limited to the places that are shared with the phone.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>How a connection reached the PC: the home network, Tailscale, or anything else.</summary>
    internal enum NetRoute { Lan, Tailnet, Other }

    /// <summary>The phone behind the current request. <see cref="Device"/> is null only for an old phone using the shared token from an unknown address.</summary>
    internal sealed record Caller(PairedDevice? Device, bool Legacy, string Ip, NetRoute Route);

    private static readonly AsyncLocal<Caller?> CurrentCaller = new();

    /// <summary>Tests decide the route of a loopback connection themselves (a test cannot connect "through Tailscale").</summary>
    internal Func<IPEndPoint?, IPEndPoint?, NetRoute>? RouteOverride;

    public static string AppVersion { get; } = typeof(LocalMobileBridgeService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>What this PC can do, so a newer phone app can use it and fall back on an older PC instead of guessing from errors.</summary>
    internal IReadOnlyList<string> Features() =>
    [
        "device-tokens", "media-url", "settings", "save-restore", "pc-control", "upload-resume", "download-range",
        "fs-search", "fs-details", "tailnet", "pair-link", "space", "space-phone", "game-stream",
        .. (ThumbnailFactory != null ? new[] { "fs-thumb" } : Array.Empty<string>())
    ];

    // ---- tokens ------------------------------------------------------------------------------------------------------

    internal static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static bool SameText(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>The phone a token belongs to, or null. A blocked or removed phone gets null whatever address it uses.</summary>
    private Caller? Authenticate(string? token, string ip, NetRoute route)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var bridge = _host.Settings.MobileBridge;
        var hash = HashToken(token);
        lock (_devicesLock)
        {
            PairedDevice? match = null;
            foreach (var d in bridge.PairedDevices)
                if (d.TokenHash is { Length: 64 } h && SameText(h, hash)) match = d; // no early exit: same time for every token
            if (match != null) return match.IsBlocked ? null : new Caller(match, false, ip, route);

            if (bridge.LegacyTokenAccepted == true && TokenMatches(token))
            {
                var byIp = bridge.PairedDevices.FirstOrDefault(d => d.IpAddress == ip && d.TokenHash == null);
                if (byIp is { IsBlocked: true } || bridge.PairedDevices.Any(d => d.IpAddress == ip && d.IsBlocked)) return null;
                return new Caller(byIp, true, ip, route);
            }
        }
        return null;
    }

    /// <summary>
    /// Adds (or updates) a phone and gives it a token of its own. The token is returned once and only its hash is kept.
    /// The same install re-pairing replaces its old entry, so one phone never shows up twice.
    /// </summary>
    internal (string Token, PairedDevice Device) IssueDeviceToken(string name, string ip, string? installId, PairedDevice? existing = null)
    {
        var token = MobileBridgeSettings.NewPairingToken();
        if (installId is { } iid && (iid.Length is < 8 or > 64 || !iid.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) installId = null;
        PairedDevice device;
        lock (_devicesLock)
        {
            var list = _host.Settings.MobileBridge.PairedDevices;
            device = existing ?? (installId != null ? list.FirstOrDefault(d => d.InstallId == installId) : null) ?? new PairedDevice();
            if (!list.Contains(device)) list.Add(device);
            device.Name = string.IsNullOrWhiteSpace(name) ? device.Name : name.Trim()[..Math.Min(60, name.Trim().Length)];
            device.IpAddress = ip;
            device.InstallId = installId ?? device.InstallId;
            device.TokenHash = HashToken(token);
            device.IsBlocked = false;
            device.LastSeen = DateTimeOffset.Now;
            if (existing == null && device.PairedAt == default) device.PairedAt = DateTimeOffset.Now;
        }
        _host.SaveSettings();
        StatusChanged?.Invoke();
        return (token, device);
    }

    /// <summary>Removes one phone: its token stops working at once.</summary>
    public void RemoveDevice(string id)
    {
        lock (_devicesLock) _host.Settings.MobileBridge.PairedDevices.RemoveAll(d => d.Id == id);
        _host.SaveSettings();
        StatusChanged?.Invoke();
    }

    /// <summary>Disconnects every phone: all device tokens and the old shared token stop working.</summary>
    public void RevokeAll()
    {
        lock (_devicesLock) _host.Settings.MobileBridge.PairedDevices.Clear();
        RetireLegacyToken();
        NewPairingSecrets();
    }

    /// <summary>Phones still using the shared token that every phone used before device tokens.</summary>
    public int LegacyPhones
    {
        get { lock (_devicesLock) return _host.Settings.MobileBridge.LegacyTokenAccepted == true ? _host.Settings.MobileBridge.PairedDevices.Count(d => d.TokenHash == null) : 0; }
    }

    public bool LegacyTokenAccepted => _host.Settings.MobileBridge.LegacyTokenAccepted == true;

    /// <summary>Turns the old shared token off for good (and replaces it, in case it was ever copied). Old-style phones pair again.</summary>
    public void RetireLegacyToken()
    {
        var bridge = _host.Settings.MobileBridge;
        bridge.LegacyTokenAccepted = false;
        bridge.PairingToken = MobileBridgeSettings.NewPairingToken();
        lock (_devicesLock) bridge.PairedDevices.RemoveAll(d => d.TokenHash == null);
        _host.SaveSettings();
        StatusChanged?.Invoke();
    }

    // ---- pairing link (QR code) -----------------------------------------------------------------------------------

    private readonly object _linkLock = new();
    private string _linkSecret = MobileBridgeSettings.NewPairingToken();
    private DateTimeOffset _linkCreated = DateTimeOffset.UtcNow;
    internal static readonly TimeSpan PairLinkLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The secret in the QR code / pairing link: works once, for ten minutes, from the home network only. It replaces
    /// the long token that used to be in the QR code, so a photo of the screen is not a permanent key.
    /// </summary>
    public string PairingLinkSecret
    {
        get
        {
            lock (_linkLock)
            {
                if (DateTimeOffset.UtcNow - _linkCreated > PairLinkLifetime) (_linkSecret, _linkCreated) = (MobileBridgeSettings.NewPairingToken(), DateTimeOffset.UtcNow);
                return _linkSecret;
            }
        }
    }

    public string PairingLinkUrl => $"{LocalUrl}?pair={PairingLinkSecret}";

    /// <summary>New six digits and a new QR link (the old ones stop working).</summary>
    public void NewPairingSecrets()
    {
        lock (_codeLock) PairingCode = NewPairingCode();
        lock (_linkLock) (_linkSecret, _linkCreated) = (MobileBridgeSettings.NewPairingToken(), DateTimeOffset.UtcNow);
        StatusChanged?.Invoke();
    }

    internal bool TryPairingLink(string? secret, string clientIp)
    {
        if (string.IsNullOrEmpty(secret) || !IsLocalNetworkAddress(clientIp)) return false;
        bool ok;
        lock (_linkLock)
        {
            ok = DateTimeOffset.UtcNow - _linkCreated <= PairLinkLifetime && SameText(secret, _linkSecret);
            if (ok) (_linkSecret, _linkCreated) = (MobileBridgeSettings.NewPairingToken(), DateTimeOffset.UtcNow);
        }
        if (ok) StatusChanged?.Invoke();
        return ok;
    }

    // ---- network route ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Decided by our own end of the connection first: a request that arrived on the PC's Tailscale address came
    /// through Tailscale, even if the sender's address happens to look like a home-network one.
    /// </summary>
    internal NetRoute ClassifyRoute(IPEndPoint? local, IPEndPoint? remote)
    {
        if (RouteOverride is { } o) return o(local, remote);
        if (TailscaleInfo.IsTailnetAddress(local?.Address)) return NetRoute.Tailnet;
        if (remote is null) return NetRoute.Other;
        if (IsLocalNetworkAddress(remote.Address.ToString()) || IsOnLocalSubnet(remote.Address)) return NetRoute.Lan;
        return TailscaleInfo.IsTailnetAddress(remote.Address) ? NetRoute.Tailnet : NetRoute.Other;
    }

    /// <summary>True when the address is on one of this PC's own IPv4 subnets (for home networks outside the usual private ranges).</summary>
    private static bool IsOnLocalSubnet(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetwork || TailscaleInfo.IsTailnetAddress(ip)) return false;
        var a = ip.GetAddressBytes();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null || TailscaleInfo.IsTailnetAddress(ua.Address)) continue;
                    var own = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    bool same = true;
                    for (int i = 0; i < 4; i++) same &= (a[i] & mask[i]) == (own[i] & mask[i]);
                    if (same) return true;
                }
            }
        }
        catch (NetworkInformationException) { }
        return false;
    }

    /// <summary>The score that picks the address shown in the QR code. Tailscale and link-local addresses never win.</summary>
    internal static int ScoreAddress(NetworkInterfaceType type, IPAddress ip)
    {
        if (TailscaleInfo.IsTailnetAddress(ip)) return -100;
        var b = ip.GetAddressBytes();
        int score = type switch { NetworkInterfaceType.Wireless80211 => 30, NetworkInterfaceType.Ethernet => 20, _ => 0 };
        if (b[0] == 192 && b[1] == 168) score += 15;
        else if (b[0] == 10) score += 10;
        else if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) score += 10;
        else if (b[0] == 169 && b[1] == 254) score -= 50;
        return score;
    }

    // ---- switches ----------------------------------------------------------------------------------------------------

    private static readonly (string Name, Func<MobileBridgeSettings, bool> Allowed)
        Browsing = ("Drive browsing", s => s.AllowDriveBrowsing),
        Streaming = ("Streaming", s => s.AllowStreaming),
        Uploads = ("Uploads", s => s.AllowUploads),
        Hud = ("Gaming HUD and PC remote", s => s.AllowGamingHud),
        FileManagement = ("File management", s => s.AllowFileManagement),
        PcControl = ("PC control", s => s.AllowPcControl),
        Space = ("Space & RAM", s => s.AllowSpace),
        GameStream = ("Play on phone", s => s.AllowGameStream);

    /// <summary>Every switch a route needs; all must be on. Routes not listed need only a paired phone.</summary>
    internal static IReadOnlyList<(string Name, Func<MobileBridgeSettings, bool> Allowed)> RequiredSwitches(string path, string method)
    {
        var p = path.ToLowerInvariant();
        bool post = method == "POST";
        // A phone's own reports and the commands waiting for it need only the pairing; seeing the PC needs the switch.
        if (p.StartsWith("/api/space/phone-")) return [];
        if (p.StartsWith("/api/space/")) return p == "/api/space/action" ? [Space, PcControl] : [Space];
        if (p.StartsWith("/api/game/")) return [GameStream];
        if (p.StartsWith("/api/fs/") || p.StartsWith("/api/backup/"))
        {
            return p switch
            {
                "/api/fs/read" => [FileManagement, Streaming],
                "/api/fs/write" or "/api/backup/upload" => [FileManagement, Uploads],
                _ => [FileManagement]
            };
        }
        return p switch
        {
            "/api/files" or "/api/preview" or "/api/drives" => [Browsing],
            "/api/stream" or "/api/download" or "/api/media-url" => [Streaming],
            "/api/open-pc" => [Streaming, PcControl],
            "/api/upload" or "/api/upload/status" => [Uploads],
            "/api/hud" or "/api/remote" => [Hud],
            "/api/drives/mount" or "/api/drives/unmount" or "/api/clean" or "/api/storage/offload-now" or "/api/transfers/clear" => [PcControl],
            "/api/storage/offload-config" when post => [PcControl],
            "/api/transfers" when post => [PcControl],
            "/api/saves" => [Hud],
            "/api/saves/download" => [Hud, Streaming],
            "/api/saves/restore" => [Hud, PcControl],
            _ => []
        };
    }

    // ---- shared places --------------------------------------------------------------------------------------------

    private static string Folderish(string p) => Path.GetFullPath(CleanPath(p)).TrimEnd('\\') + "\\";

    /// <summary>True when <paramref name="fullPath"/> is one of <paramref name="roots"/> or inside one ("C:\up" is not inside "C:\u").</summary>
    internal static bool IsInsideAny(string fullPath, IEnumerable<string> roots)
    {
        string p;
        try { p = Folderish(fullPath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            try
            {
                if (p.StartsWith(Folderish(r), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return false;
    }

    /// <summary>The folders a paired phone may read: the connected cloud drives, the folders phone files are saved to, and (only when switched on) the PC's own disks.</summary>
    private IEnumerable<string> SharedRoots()
    {
        foreach (var r in CloudRoots()) yield return r.Root;
        yield return UploadDirectory;
        if (_host.Settings.MobileBridge.PhotoBackupDirectory is { Length: > 0 } photos) yield return photos;
        if (!_host.Settings.MobileBridge.AllowPcDisks) yield break;
        DriveInfo[] disks;
        try { disks = DriveInfo.GetDrives(); }
        catch (IOException) { yield break; }
        foreach (var d in disks)
            if (d.DriveType == DriveType.Fixed) yield return d.Name;
    }

    private sealed record SharedRefusal(int Status, object Body);

    /// <summary>
    /// A path from the phone, as a full local path inside a shared place — or why not: 400 for a malformed path, 404
    /// for a drive that is not connected (nothing is read), 403 for a place that is not shared with the phone.
    /// </summary>
    private (string? Path, SharedRefusal? Refusal) ResolveShared(string? raw)
    {
        var p = ResolveLocalPath(raw);
        if (p is null) return (null, new SharedRefusal(400, new { error = "Invalid path" }));
        var clean = CleanPath(p);
        var root = Path.GetPathRoot(clean) ?? "";
        if (root.Length > 0 && !Directory.Exists(root))
            return (null, new SharedRefusal(404, new { error = $"Drive {root.TrimEnd('\\')} is not connected on the PC.", path = clean, driveMissing = true }));
        if (!IsInsideAny(clean, SharedRoots()))
            return (null, new SharedRefusal(403, new { error = "This place is not shared with the phone. Turn on 'PC disks' in Cloud-SSD → Phone → Permissions on the PC.", code = "not-shared" }));
        return (p, null);
    }

    private static Task SendRefusalAsync(NetworkStream stream, SharedRefusal r, CancellationToken ct) =>
        SendJsonAsync(stream, r.Status, StatusText(r.Status), r.Body, ct);

    // ---- signed file links ----------------------------------------------------------------------------------------

    internal static readonly TimeSpan StreamLinkLifetime = TimeSpan.FromHours(12), DownloadLinkLifetime = TimeSpan.FromHours(1);

    private byte[] MediaKey()
    {
        var bridge = _host.Settings.MobileBridge;
        if (string.IsNullOrEmpty(bridge.MediaKey))
        {
            bridge.MediaKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _host.SaveSettings();
        }
        return Convert.FromBase64String(bridge.MediaKey);
    }

    internal string MediaSignature(string kind, string path, string device, long exp)
    {
        var mac = HMACSHA256.HashData(MediaKey(), Encoding.UTF8.GetBytes($"{kind}\n{path}\n{device}\n{exp}"));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>A link to one file that works without the token for a limited time, and stops working when the phone is removed.</summary>
    internal string SignedMediaUrl(string kind, string fullPath, Caller caller)
    {
        string device = caller.Device?.Id ?? "legacy";
        long exp = DateTimeOffset.UtcNow.Add(kind == "download" ? DownloadLinkLifetime : StreamLinkLifetime).ToUnixTimeSeconds();
        string sig = MediaSignature(kind, fullPath, device, exp);
        return $"/api/{kind}?path={Uri.EscapeDataString(fullPath)}&d={Uri.EscapeDataString(device)}&exp={exp}&sig={sig}";
    }

    /// <summary>Checks a signed link for exactly this route and path. Null when it is wrong, expired or its phone was removed.</summary>
    private Caller? VerifySignedUrl(string path, Dictionary<string, string> q, string ip, NetRoute route)
    {
        var kind = path.Equals("/api/stream", StringComparison.OrdinalIgnoreCase) ? "stream"
            : path.Equals("/api/download", StringComparison.OrdinalIgnoreCase) ? "download" : null;
        if (kind is null || q.GetValueOrDefault("sig") is not { Length: > 0 } sig || q.GetValueOrDefault("d") is not { Length: > 0 } device
            || q.GetValueOrDefault("path") is not { Length: > 0 } file
            || !long.TryParse(q.GetValueOrDefault("exp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp)) return null;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (exp < now || exp > now + (long)StreamLinkLifetime.TotalSeconds + 60) return null;
        if (!SameText(sig, MediaSignature(kind, file, device, exp))) return null;
        var bridge = _host.Settings.MobileBridge;
        if (device == "legacy") return bridge.LegacyTokenAccepted == true ? new Caller(null, true, ip, route) : null;
        lock (_devicesLock)
        {
            var d = bridge.PairedDevices.FirstOrDefault(x => x.Id == device);
            return d is { IsBlocked: false, TokenHash: not null } ? new Caller(d, false, ip, route) : null;
        }
    }

    private async Task HandleMediaUrlEndpointAsync(Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        var kind = q.GetValueOrDefault("kind") is "download" ? "download" : "stream";
        string? full;
        if (q.GetValueOrDefault("id") is { Length: > 0 } id)
        {
            try { full = ResolveCloudPath(CloudRoots(), id).FullPath; }
            catch (FsException e)
            {
                await SendJsonAsync(stream, e.Status, StatusText(e.Status), new { error = e.Message }, ct);
                return;
            }
        }
        else
        {
            var (p, refusal) = ResolveShared(q.GetValueOrDefault("path"));
            if (refusal != null) { await SendRefusalAsync(stream, refusal, ct); return; }
            full = CleanPath(p!);
        }
        if (!File.Exists(full))
        {
            await SendJsonAsync(stream, 404, "Not Found", new { error = "File not found" }, ct);
            return;
        }
        var caller = CurrentCaller.Value!;
        var url = SignedMediaUrl(kind, full, caller);
        await SendJsonAsync(stream, 200, "OK", new
        {
            url,
            expiresAt = DateTimeOffset.UtcNow.Add(kind == "download" ? DownloadLinkLifetime : StreamLinkLifetime).ToUnixTimeMilliseconds()
        }, ct);
    }

    // ---- phone settings and devices ------------------------------------------------------------------------------

    private object PermissionsDto()
    {
        var b = _host.Settings.MobileBridge;
        return new
        {
            driveBrowsing = b.AllowDriveBrowsing, streaming = b.AllowStreaming, uploads = b.AllowUploads,
            fileManagement = b.AllowFileManagement, gamingHud = b.AllowGamingHud, pcControl = b.AllowPcControl,
            pcDisks = b.AllowPcDisks, tailscale = b.AllowTailscale, space = b.AllowSpace
        };
    }

    private object DevicesDto(Caller caller)
    {
        lock (_devicesLock)
        {
            return _host.Settings.MobileBridge.PairedDevices.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                pairedAt = d.PairedAt,
                lastSeen = d.LastSeen,
                isBlocked = d.IsBlocked,
                isThisPhone = caller.Device == d || (caller.Device == null && d.IpAddress == caller.Ip && d.TokenHash == null),
                legacy = d.TokenHash == null
            }).ToList();
        }
    }

    private object AwayDto()
    {
        var b = _host.Settings.MobileBridge;
        if (!b.AllowTailscale) return new { allowed = false, ips = Array.Empty<string>(), dnsName = (string?)null };
        var ts = TailscaleInfo.Current();
        return new { allowed = true, ips = ts.Ips, dnsName = ts.DnsName };
    }

    private object SettingsDto(Caller caller) => new
    {
        version = AppVersion,
        pcName = Environment.MachineName,
        pcId = _host.Settings.MobileBridge.PcId,
        features = Features(),
        permissions = PermissionsDto(),
        devices = DevicesDto(caller),
        device = caller.Device is { } d ? new { id = d.Id, name = d.Name } : null,
        auth = caller.Legacy ? "legacy" : "device",
        offload = _host.Offloader.GetStatus(),
        offloadTargets = _host.Settings.Mounts.Select(m => new { id = m.Id, name = m.DisplayName }).ToList(),
        away = AwayDto(),
        route = RouteName(caller.Route)
    };

    private static string RouteName(NetRoute r) => r switch { NetRoute.Tailnet => "tailnet", NetRoute.Lan => "lan", _ => "other" };

    private async Task HandleDevicesRemoveAsync(string method, Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        var caller = CurrentCaller.Value!;
        if (method != "POST")
        {
            await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct);
            return;
        }
        var id = q.GetValueOrDefault("id") ?? "self";
        var own = caller.Device?.Id;
        if (id != "self" && id != own)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "A phone can only remove itself. Other phones are removed on the PC." }, ct);
            return;
        }
        if (own != null) RemoveDevice(own);
        else
        {
            lock (_devicesLock) _host.Settings.MobileBridge.PairedDevices.RemoveAll(d => d.IpAddress == caller.Ip && d.TokenHash == null);
            _host.SaveSettings();
            StatusChanged?.Invoke();
        }
        await SendJsonAsync(stream, 200, "OK", new { success = true }, ct);
    }

    /// <summary>An old-style phone (shared token) gets a token of its own; after that the shared token can be turned off.</summary>
    private async Task HandlePairUpgradeAsync(string method, Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        var caller = CurrentCaller.Value!;
        if (method != "POST")
        {
            await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct);
            return;
        }
        if (!caller.Legacy)
        {
            await SendJsonAsync(stream, 200, "OK", new { success = true, upgraded = false, deviceId = caller.Device?.Id }, ct);
            return;
        }
        var (token, device) = IssueDeviceToken(q.GetValueOrDefault("name") ?? caller.Device?.Name ?? "Android Device", caller.Ip, q.GetValueOrDefault("installId"), caller.Device);
        await SendJsonAsync(stream, 200, "OK", new { success = true, upgraded = true, token, deviceId = device.Id, deviceName = device.Name }, ct);
    }

    private static string OsDescription => RuntimeInformation.OSDescription;
}
