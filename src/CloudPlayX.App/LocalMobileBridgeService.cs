using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudPlayX.Core;
using CloudPlayX.Mount;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.App;

/// <summary>
/// Embedded zero-admin local HTTP &amp; streaming bridge that connects Android mobile devices to Cloud-SSD
/// over local Wi-Fi or phone hotspot with 0 internet data consumed.
/// </summary>
public sealed class LocalMobileBridgeService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppHost _host;
    private readonly ILogger<LocalMobileBridgeService> _log;
    // At most this many connections at once; more are closed straight away instead of piling up.
    private readonly SemaphoreSlim _connections = new(64);
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private Task? _acceptTask;
    private long _bytesTransferred;
    private long _lastRateSampleBytes;
    private DateTimeOffset _lastRateSampleTime = DateTimeOffset.UtcNow;
    private double _currentRateMbps;
    private readonly List<TransferRecord> _transferHistory = new();
    private readonly object _historyLock = new();
    private string HistoryFilePath => Path.Combine(_host.Paths.DataRoot, "transfer_history.json");

    public LocalMobileBridgeService(AppHost host, ILogger<LocalMobileBridgeService> log)
    {
        _host = host;
        _log = log;
        LoadTransferHistory();
    }

    public bool IsRunning => _listener != null && (_cts != null && !_cts.IsCancellationRequested);
    public int Port => _host.Settings.MobileBridge.Port;
    public string PairingToken => _host.Settings.MobileBridge.PairingToken;

    /// <summary>
    /// Six digits shown in Mobile Hub. A phone on the local network types them once and gets the long token back, so
    /// nobody has to type 32 characters. Used once, then replaced; wrong guesses lock code pairing for a minute.
    /// </summary>
    public string PairingCode { get; private set; } = NewPairingCode();
    private readonly object _codeLock = new();
    private int _codeFailures;
    private DateTimeOffset _codeLockedUntil;
    internal const int MaxCodeFailures = 5;
    internal static readonly TimeSpan CodeLockout = TimeSpan.FromSeconds(60);

    internal static string NewPairingCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    public double CurrentTransferRateMbps => _currentRateMbps;
    public long TotalBytesTransferred => Interlocked.Read(ref _bytesTransferred);

    public event Action? StatusChanged;
    public event Action<string, long>? FileTransferred;
    public event Action<TransferRecord>? TransferRecorded;

    public string LocalIpAddress => GetPreferredLocalIp();

    public string LocalUrl => $"http://{LocalIpAddress}:{Port}/";

    public static string GetPreferredLocalIp()
    {
        try
        {
            var candidates = new List<(IPAddress Ip, int Score)>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address;
                    if (IPAddress.IsLoopback(ip)) continue;

                    var bytes = ip.GetAddressBytes();
                    // Score based on interface type and subnet
                    int score = 0;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 30;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 20;

                    // Typical local subnets
                    if (bytes[0] == 192 && bytes[1] == 168) score += 15;
                    else if (bytes[0] == 10) score += 10;
                    else if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) score += 10;
                    else if (bytes[0] == 169 && bytes[1] == 254) score -= 50; // Link-local autoconfig

                    candidates.Add((ip, score));
                }
            }

            if (candidates.Count > 0)
            {
                candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
                return candidates[0].Ip.ToString();
            }
        }
        catch
        {
            // Fallback
        }

        return "127.0.0.1";
    }

    public static IReadOnlyList<string> GetAllLocalIps()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                    {
                        list.Add(ua.Address.ToString());
                    }
                }
            }
        }
        catch { }
        if (list.Count == 0) list.Add("127.0.0.1");
        return list;
    }

    public void Start()
    {
        if (IsRunning) return;

        if (PairingToken.Length < MobileBridgeSettings.MinPairingTokenLength)
        {
            // v0.7.0 tokens had only 48 bits; replace them. Paired phones scan the new QR code once.
            _host.Settings.MobileBridge.PairingToken = MobileBridgeSettings.NewPairingToken();
            _host.SaveSettings();
            _log.LogWarning("Mobile Bridge pairing token upgraded to 128 bits; phones need to scan the QR code again");
        }

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _acceptTask = Task.Run(AcceptLoopAsync);
            _log.LogInformation("Cloud-SSD Mobile Bridge listening on 0.0.0.0:{Port} (Access at {Url})", Port, LocalUrl);
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start Cloud-SSD Mobile Bridge on port {Port}", Port);
            Stop();
        }
    }

    public void Stop()
    {
        if (!IsRunning && _listener == null) return;

        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Exception while stopping Mobile Bridge listener");
        }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            StatusChanged?.Invoke();
            _log.LogInformation("Cloud-SSD Mobile Bridge stopped");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_acceptTask != null)
        {
            try { await _acceptTask; } catch { }
        }
        _telemetry.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                if (!_connections.Wait(0))
                {
                    client.Dispose();
                    continue;
                }
                client.NoDelay = true;
                _ = Task.Run(async () =>
                {
                    try { await HandleClientAsync(client, ct); }
                    finally { _connections.Release(); }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Error accepting client connection");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var networkStream = client.GetStream();

                // Read request headers byte-by-byte into buffer until \r\n\r\n (avoids StreamReader buffering body bytes)
                var headerBytes = new List<byte>(512);
                byte[] oneByte = new byte[1];
                // A client that never finishes its headers must not hold the connection forever.
                using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                headerTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                while (await networkStream.ReadAsync(oneByte.AsMemory(0, 1), headerTimeout.Token) > 0)
                {
                    headerBytes.Add(oneByte[0]);
                    int count = headerBytes.Count;
                    if (count >= 4 &&
                        headerBytes[count - 4] == (byte)'\r' &&
                        headerBytes[count - 3] == (byte)'\n' &&
                        headerBytes[count - 2] == (byte)'\r' &&
                        headerBytes[count - 1] == (byte)'\n')
                    {
                        break;
                    }
                    if (count > 32 * 1024) return;
                }

                if (headerBytes.Count == 0) return;

                var headerText = Encoding.UTF8.GetString(headerBytes.ToArray());
                var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) return;

                var parts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return;

                var method = parts[0].ToUpperInvariant();
                var rawUrl = parts[1];

                // Parse headers
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                    }
                }

                var clientEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
                var clientIp = clientEndpoint?.Address.ToString() ?? "unknown";

                // DNS rebinding guard: a web page can only reach us through a host name it controls, never an IP literal.
                if (!IsAllowedHost(headers.GetValueOrDefault("Host")))
                {
                    await SendResponseAsync(networkStream, 421, "Misdirected Request", "text/plain", Encoding.UTF8.GetBytes("Unknown host"), ct);
                    return;
                }

                // Handle CORS preflight
                if (method == "OPTIONS")
                {
                    await SendResponseAsync(networkStream, 204, "No Content", "text/plain", Array.Empty<byte>(), ct);
                    return;
                }

                // Parse URL & Query params
                int queryIdx = rawUrl.IndexOf('?');
                string path = queryIdx >= 0 ? rawUrl[..queryIdx] : rawUrl;
                string query = queryIdx >= 0 ? rawUrl[(queryIdx + 1)..] : "";
                var queryParams = ParseQuery(query);

                // Check authorization for API routes
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleApiRouteAsync(method, path, queryParams, headers, networkStream, clientIp, ct);
                }
                else
                {
                    // Serve static PWA assets
                    await HandleStaticRouteAsync(path, networkStream, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "IO exception handling client HTTP request");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error handling client HTTP request");
            }
        }
    }

    private async Task HandleApiRouteAsync(
        string method,
        string path,
        Dictionary<string, string> queryParams,
        Dictionary<string, string> headers,
        NetworkStream stream,
        string clientIp,
        CancellationToken ct)
    {
        // Check token / pairing
        var token = queryParams.GetValueOrDefault("token")
            ?? headers.GetValueOrDefault("X-Pairing-Token")
            ?? (headers.GetValueOrDefault("Authorization")?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? headers["Authorization"][7..].Trim() : null);

        // Every request carries the token: being on the same Wi-Fi, on a previously paired IP or on this PC is not enough.
        // (128 random bits, so guessing needs no rate limit — and a limit would lock out phones polling with an old token.)
        bool isPaired = TokenMatches(token) && !IsBlocked(clientIp);

        if (path.Equals("/api/pair", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            await HandlePairEndpointAsync(queryParams, headers, stream, clientIp, ct);
            return;
        }

        if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStatusEndpointAsync(stream, isPaired, ct);
            return;
        }

        if (!isPaired)
        {
            await SendJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized. Please scan the QR code to pair your device." }, ct);
            return;
        }

        // Update LastSeen for paired device
        UpdateDeviceLastSeen(clientIp);

        var bridge = _host.Settings.MobileBridge;
        if (Feature(path) is { } feature && !feature.allowed(bridge))
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = $"{feature.name} is switched off in Cloud-SSD → Mobile Hub settings." }, ct);
            return;
        }

        if (path.Equals("/api/hud", StringComparison.OrdinalIgnoreCase))
        {
            await HandleHudEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/drives", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDrivesEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/drives/mount", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDriveMountEndpointAsync(method, queryParams, stream, ct);
        }
        else if (path.Equals("/api/drives/unmount", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDriveUnmountEndpointAsync(method, queryParams, stream, ct);
        }
        else if (path.Equals("/api/files", StringComparison.OrdinalIgnoreCase))
        {
            await HandleFilesEndpointAsync(queryParams, stream, ct);
        }
        else if (path.Equals("/api/stream", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStreamEndpointAsync(queryParams, headers, stream, clientIp, ct);
        }
        else if (path.Equals("/api/download", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDownloadEndpointAsync(queryParams, stream, clientIp, ct);
        }
        else if (path.Equals("/api/upload", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            await HandleUploadEndpointAsync(queryParams, headers, stream, clientIp, ct);
        }
        else if (path.Equals("/api/transfers", StringComparison.OrdinalIgnoreCase))
        {
            if (method == "POST" && (queryParams.GetValueOrDefault("action") == "clear" || path.EndsWith("/clear", StringComparison.OrdinalIgnoreCase)))
            {
                await HandleTransfersClearEndpointAsync(stream, ct);
            }
            else
            {
                await HandleTransfersEndpointAsync(stream, ct);
            }
        }
        else if (path.Equals("/api/transfers/clear", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTransfersClearEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/saves", StringComparison.OrdinalIgnoreCase))
        {
            await HandleSavesEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/devices", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDevicesEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/clean", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCleanEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/open-pc", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOpenPcEndpointAsync(queryParams, stream, ct);
        }
        else if (path.Equals("/api/preview", StringComparison.OrdinalIgnoreCase))
        {
            await HandlePreviewEndpointAsync(queryParams, stream, ct);
        }
        else if (path.Equals("/api/storage/offload-config", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOffloadConfigEndpointAsync(method, queryParams, headers, stream, ct);
        }
        else if (path.Equals("/api/storage/offload-now", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOffloadNowEndpointAsync(stream, ct);
        }
        else if (path.Equals("/api/remote", StringComparison.OrdinalIgnoreCase))
        {
            await HandleRemoteEndpointAsync(method, queryParams, stream, ct);
        }
        else
        {
            var notFoundJson = JsonSerializer.Serialize(new { error = "API endpoint not found" }, JsonOpts);
            await SendResponseAsync(stream, 404, "Not Found", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(notFoundJson), ct);
        }
    }

    private bool TokenMatches(string? token) =>
        !string.IsNullOrEmpty(token) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(PairingToken));

    private bool IsBlocked(string clientIp)
    {
        lock (_devicesLock) return _host.Settings.MobileBridge.PairedDevices.Any(d => d.IpAddress == clientIp && d.IsBlocked);
    }

    private void UpdateDeviceLastSeen(string clientIp)
    {
        lock (_devicesLock)
        {
            var device = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.IpAddress == clientIp);
            if (device != null) device.LastSeen = DateTimeOffset.Now;
        }
    }

    private readonly object _devicesLock = new();

    /// <summary>
    /// The Host header must name this PC by IP literal, "localhost" or its machine name. A malicious web page can only
    /// point a DNS name it owns at us (DNS rebinding), so anything else is refused.
    /// </summary>
    internal static bool IsAllowedHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return true; // HTTP/1.0 clients; browsers always send Host
        var host = hostHeader.Trim();
        if (host.StartsWith('['))
            host = host[1..Math.Max(1, host.IndexOf(']'))];               // [::1]:8090
        else if (host.Count(c => c == ':') == 1)
            host = host[..host.IndexOf(':')];                            // 192.168.1.5:8090
        return IPAddress.TryParse(host, out _)
               || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static (string name, Func<MobileBridgeSettings, bool> allowed)? Feature(string path) => path.ToLowerInvariant() switch
    {
        "/api/files" or "/api/preview" or "/api/drives" => ("Drive browsing", s => s.AllowDriveBrowsing),
        "/api/stream" or "/api/download" or "/api/open-pc" => ("Streaming", s => s.AllowStreaming),
        "/api/upload" => ("Uploads", s => s.AllowUploads),
        "/api/hud" or "/api/remote" => ("Gaming HUD and PC remote", s => s.AllowGamingHud),
        _ => null
    };

    private static Task SendJsonAsync(NetworkStream stream, int status, string statusText, object body, CancellationToken ct) =>
        SendResponseAsync(stream, status, statusText, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(body, JsonOpts), ct);

    private async Task HandlePairEndpointAsync(
        Dictionary<string, string> queryParams,
        Dictionary<string, string> headers,
        NetworkStream stream,
        string clientIp,
        CancellationToken ct)
    {
        string? reqToken = queryParams.GetValueOrDefault("token");
        string? reqCode = queryParams.GetValueOrDefault("code");
        string deviceName = queryParams.GetValueOrDefault("name", "Android Device");

        if (headers.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out int cl) && cl > 0 && cl < 10000)
        {
            byte[] buf = new byte[cl];
            int readTotal = 0;
            while (readTotal < cl)
            {
                int r = await stream.ReadAsync(buf.AsMemory(readTotal, cl - readTotal), ct);
                if (r == 0) break;
                readTotal += r;
            }
            var body = Encoding.UTF8.GetString(buf, 0, readTotal);
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("token", out var tProp)) reqToken = tProp.GetString();
                if (doc.RootElement.TryGetProperty("code", out var cProp)) reqCode = cProp.GetString();
                if (doc.RootElement.TryGetProperty("name", out var nProp)) deviceName = nProp.GetString() ?? deviceName;
            }
            catch { }
        }
        if (deviceName.Length > 60) deviceName = deviceName[..60];

        bool byCode = false;
        if (!TokenMatches(reqToken) && !string.IsNullOrWhiteSpace(reqCode))
        {
            var (ok, retryAfter) = TryPairingCode(reqCode, clientIp);
            if (retryAfter > 0)
            {
                await SendJsonAsync(stream, 429, "Too Many Requests", new { success = false, error = $"Too many wrong codes. Try again in {retryAfter} seconds with the new code shown on the PC.", retryAfterSeconds = retryAfter }, ct);
                return;
            }
            byCode = ok;
        }

        if ((byCode || TokenMatches(reqToken)) && !IsBlocked(clientIp))
        {
            lock (_devicesLock)
            {
                var existing = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.IpAddress == clientIp);
                if (existing != null)
                {
                    existing.LastSeen = DateTimeOffset.Now;
                    existing.Name = deviceName;
                }
                else
                {
                    _host.Settings.MobileBridge.PairedDevices.Add(new PairedDevice
                    {
                        Name = deviceName,
                        IpAddress = clientIp,
                        PairedAt = DateTimeOffset.Now,
                        LastSeen = DateTimeOffset.Now,
                        IsBlocked = false
                    });
                }
            }

            _host.SaveSettings();
            StatusChanged?.Invoke();

            var resp = JsonSerializer.Serialize(new
            {
                success = true,
                message = "Device paired successfully with Cloud-SSD",
                token = PairingToken,
                deviceName,
                pcName = Environment.MachineName
            }, JsonOpts);

            await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(resp), ct);
        }
        else
        {
            var resp = JsonSerializer.Serialize(new { success = false, error = string.IsNullOrWhiteSpace(reqCode) ? "Invalid pairing token." : "Wrong pairing code. Check the 6 digits shown in Cloud-SSD → Phone on the PC." }, JsonOpts);
            await SendResponseAsync(stream, 403, "Forbidden", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(resp), ct);
        }
    }

    /// <summary>
    /// Checks a typed 6-digit code. Only devices on the local network may try, a right code is used up, and
    /// <see cref="MaxCodeFailures"/> wrong ones replace the code and pause code pairing (RetryAfterSeconds &gt; 0).
    /// </summary>
    internal (bool Ok, int RetryAfterSeconds) TryPairingCode(string code, string clientIp)
    {
        if (!IsLocalNetworkAddress(clientIp)) return (false, 0);
        bool ok;
        lock (_codeLock)
        {
            var wait = _codeLockedUntil - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return (false, (int)Math.Ceiling(wait.TotalSeconds));

            var digits = new string(code.Where(char.IsAsciiDigit).ToArray());
            ok = digits.Length == PairingCode.Length &&
                 CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(digits), Encoding.ASCII.GetBytes(PairingCode));
            if (ok)
            {
                _codeFailures = 0;
                PairingCode = NewPairingCode();
            }
            else if (++_codeFailures >= MaxCodeFailures)
            {
                _codeFailures = 0;
                PairingCode = NewPairingCode();
                _codeLockedUntil = DateTimeOffset.UtcNow + CodeLockout;
                _log.LogWarning("Mobile Bridge: {Count} wrong pairing codes from {Ip}; code replaced, code pairing paused for {Seconds} s", MaxCodeFailures, clientIp, CodeLockout.TotalSeconds);
            }
        }
        StatusChanged?.Invoke(); // Mobile Hub shows the new code
        return (ok, 0);
    }

    /// <summary>Loopback, private (10/8, 172.16/12, 192.168/16), link-local and IPv6 unique-local addresses.</summary>
    internal static bool IsLocalNetworkAddress(string? ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return false;
        if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
        if (IPAddress.IsLoopback(addr)) return true;
        if (addr.AddressFamily == AddressFamily.InterNetworkV6) return addr.IsIPv6LinkLocal || addr.IsIPv6UniqueLocal;
        var b = addr.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private async Task HandleStatusEndpointAsync(NetworkStream stream, bool isPaired, CancellationToken ct)
    {
        if (!isPaired)
        {
            // Enough for the phone to find the PC and ask for pairing; nothing about drives, games or storage.
            await SendJsonAsync(stream, 200, "OK", new { pcName = Environment.MachineName, version = "0.7.0", port = Port, isPaired = false }, ct);
            return;
        }

        var metrics = _host.Metrics.Latest;
        double downMbps = metrics.DownloadBytesPerSec is { } bps ? Bandwidth.BytesPerSecondToMbps(bps) : 0;
        string? activeGame = _host.CurrentLaunch?.Game.Name;

        var offloadStatus = _host.Offloader.GetStatus();

        var status = new
        {
            pcName = Environment.MachineName,
            storage = new
            {
                laptopFreeGb = offloadStatus.LaptopFreeSpaceGB,
                laptopTotalGb = offloadStatus.LaptopTotalSpaceGB,
                thresholdGb = offloadStatus.MinFreeSpaceThresholdGB,
                isUnderThreshold = offloadStatus.IsUnderPressure,
                autoOffloadEnabled = offloadStatus.Enabled,
                targetDrive = offloadStatus.TargetDriveName,
                totalOffloadedFormatted = offloadStatus.TotalBytesOffloadedFormatted
            },
            os = "Windows 11 / Cloud-SSD Engine",
            version = "0.7.0",
            localIp = LocalIpAddress,
            port = Port,
            isPaired,
            activeGame,
            gameRunning = activeGame != null,
            stats = new
            {
                downloadMbps = Math.Round(downMbps, 1),
                transferSpeedMbps = Math.Round(_currentRateMbps, 1),
                totalTransferredBytes = TotalBytesTransferred,
                activePrefetches = _host.Engine.Scheduler.Active,
                queuedPrefetches = _host.Engine.Queue.Count,
                cacheHitRate = CacheHitRate(metrics),
                fps = (int?)null // no frame-rate source yet (would need PresentMon/ETW)
            },
            // No disk calls here: the phone polls this every few seconds.
            drives = _host.Settings.Mounts.Select(m =>
            {
                var s = _host.Mounts.Get(m.Id);
                return new
                {
                    id = m.Id,
                    displayName = m.DisplayName,
                    driveLetter = (s?.DriveLetter.ToString() ?? m.DriveLetter ?? "?") + ":",
                    isMounted = s is { State: MountState.Mounted }
                };
            }).ToList(),
            drivesCount = _host.Mounts.Sessions.Count(s => s.State == MountState.Mounted),
            drivesTotal = _host.Settings.Mounts.Count,
            cloud = CloudStorageSummary(),
            uploadDirectory = UploadDirectory,
            devicesCount = _host.Settings.MobileBridge.PairedDevices.Count(d => !d.IsBlocked)
        };

        var json = JsonSerializer.Serialize(status, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private async Task HandleHudEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        var metrics = _host.Metrics.Latest;
        var activeGame = _host.CurrentLaunch?.Game.Name;
        double ssdSpeedMbps = metrics.DownloadBytesPerSec is { } bps ? Bandwidth.BytesPerSecondToMBps(bps) : 0;

        var sys = _telemetry.Sample();
        // Values the PC cannot measure are null (the phone shows "—"), never made up.
        var hud = new
        {
            activeGame = activeGame ?? "No Active Game",
            isGaming = activeGame != null,
            fps = (int?)null,
            ssdReadMBps = Math.Round(ssdSpeedMbps, 1),
            cacheHitRate = CacheHitRate(metrics),
            activePrefetches = _host.Engine.Scheduler.Active,
            queuedBlocks = _host.Engine.Queue.Count,
            cpuLoad = sys.CpuLoadPercent,
            ramUsedPercent = sys.RamUsedPercent,
            gpuLoad = sys.GpuLoadPercent,
            cpuTempC = sys.CpuTempC,
            gpuTempC = (double?)null
        };

        var json = JsonSerializer.Serialize(hud, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private readonly SystemTelemetry _telemetry = new();

    private string UploadDirectory => _host.Settings.MobileBridge.UploadDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD_Uploads");

    /// <summary>Real sign-ins only (the generated union/combine remotes are views of these, not extra storage).</summary>
    private IEnumerable<CloudAccountInfo> RealAccounts() =>
        _host.Remotes.Where(r => !AppHost.IsGeneratedType(r.Type) && !string.IsNullOrWhiteSpace(r.Type))
            .Select(r => _host.Accounts.GetValueOrDefault(r.Name) ?? new CloudAccountInfo(r.Name, _host.Settings.AccountEmails.GetValueOrDefault(r.Name), null, null, null, null, false));

    private object AccountDto(CloudAccountInfo a) => new
    {
        name = a.Email ?? _host.AccountName(a.Remote),
        displayName = a.DisplayName,
        usedBytes = a.UsedBytes,
        totalBytes = a.TotalBytes,
        freeBytes = a.FreeBytes ?? (a.TotalBytes is { } t && a.UsedBytes is { } u ? Math.Max(0, t - u) : null),
        reachable = a.Reachable
    };

    /// <summary>
    /// Used/total space over all connected accounts, as last reported by the providers. Asking the providers takes a
    /// few seconds, so numbers older than <paramref name="maxAge"/> are refreshed in the background and the next call
    /// gets them. Null numbers mean "not known yet", never a made-up size.
    /// </summary>
    private object CloudStorageSummary(TimeSpan? maxAge = null)
    {
        var age = maxAge ?? TimeSpan.FromMinutes(5);
        if (_host.Rclone != null && DateTime.UtcNow - _host.AccountsRefreshedAt > age && Interlocked.CompareExchange(ref _accountsRefreshing, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try { await _host.RefreshAccountsAsync(CancellationToken.None, age); }
                catch (Exception e) { _log.LogDebug(e, "Background account refresh failed"); }
                finally { Interlocked.Exchange(ref _accountsRefreshing, 0); }
            });
        }

        var accounts = RealAccounts().ToList();
        var known = accounts.Where(a => a.TotalBytes is > 0).ToList();
        long? total = known.Count > 0 ? known.Sum(a => a.TotalBytes!.Value) : null;
        // "Free" is what the provider reports (Google already subtracts Gmail and Photos); "used" is the rest, so
        // the two always add up to the total.
        long? free = known.Count > 0 ? known.Sum(a => a.FreeBytes ?? Math.Max(0, a.TotalBytes!.Value - (a.UsedBytes ?? 0))) : null;
        return new
        {
            accountsCount = accounts.Count,
            usedBytes = total - free,
            totalBytes = total,
            freeBytes = free,
            refreshedAt = _host.AccountsRefreshedAt == default ? (DateTime?)null : _host.AccountsRefreshedAt,
            accounts = accounts.Select(AccountDto).ToList()
        };
    }

    private int _accountsRefreshing;

    private static double? CacheHitRate(EngineMetrics m) =>
        m.CacheHits is { } h && m.CacheMisses is { } miss && h + miss > 0 ? Math.Round(100.0 * h / (h + miss), 1) : null;

    /// <summary>
    /// Turns a path sent by the phone into a full local path on a drive letter, or null. Refuses UNC/device paths
    /// (\\server\share would make Windows send this user's login hash to that server), NTFS alternate data streams and
    /// anything that is not rooted, so ".." tricks are resolved before any check.
    /// </summary>
    internal static string? ResolveLocalPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var p = raw.Trim().Trim('"', '\'').Replace('/', '\\');
        if (p.StartsWith(@"\\") || p.Length < 3 || !char.IsAsciiLetter(p[0]) || p[1] != ':' || p[2] != '\\') return null;
        if (p.IndexOf(':', 2) >= 0) return null;
        try { return NormalizePath(Path.GetFullPath(p)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string NormalizePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return rawPath;
        string path = rawPath.Trim('"', '\'');
        if (Directory.Exists(path) || File.Exists(path)) return path;

        string longPath = path.StartsWith(@"\\?\") ? path : @"\\?\" + path;
        if (Directory.Exists(longPath) || File.Exists(longPath)) return longPath;

        try
        {
            string clean = path.TrimEnd('\\', '/');
            int lastSep = clean.LastIndexOfAny(new[] { '\\', '/' });
            if (lastSep > 0)
            {
                string parentPart = clean.Substring(0, lastSep);
                if (parentPart.EndsWith(":")) parentPart += "\\";
                string childPart = clean.Substring(lastSep + 1).Trim();

                if (Directory.Exists(parentPart))
                {
                    var parentDi = new DirectoryInfo(parentPart);
                    var dirMatch = parentDi.EnumerateDirectories()
                        .FirstOrDefault(d => string.Equals(d.Name.Trim(), childPart, StringComparison.OrdinalIgnoreCase));
                    if (dirMatch != null) return dirMatch.FullName;

                    var fileMatch = parentDi.EnumerateFiles()
                        .FirstOrDefault(f => string.Equals(f.Name.Trim(), childPart, StringComparison.OrdinalIgnoreCase));
                    if (fileMatch != null) return fileMatch.FullName;
                }
            }
        }
        catch { }

        return path;
    }

    private static string CleanPath(string p) => p.StartsWith(@"\\?\") ? p.Substring(4) : p;

    /// <summary>What a drive really is, from its rclone backend — not guessed from its name.</summary>
    private string ConnectionLabel(string remoteName) => _host.RemoteTypeOf(remoteName) switch
    {
        "drive" => "Google Drive",
        "union" => $"Combined drive · {_host.AccountsBehind(remoteName).Count} accounts",
        "combine" => $"One folder per account · {_host.AccountsBehind(remoteName).Count} accounts",
        "onedrive" => "Microsoft OneDrive",
        "webdav" => "WebDAV",
        { } other => other,
        null => "Cloud drive"
    };

    private static (long? Total, long? Free) DiskSpace(string root)
    {
        try
        {
            var d = new DriveInfo(root);
            if (d.IsReady) return (d.TotalSize, d.AvailableFreeSpace);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return (null, null);
    }

    private async Task HandleDrivesEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        // The phone is looking at storage right now: refresh stale quota numbers in the background for its next poll.
        CloudStorageSummary(TimeSpan.FromSeconds(45));

        // Configured cloud drives (connected or not), then connected ones that are not in settings (adopted mounts).
        var cloud = _host.Settings.Mounts.Select(m => (Def: m, Session: _host.Mounts.Get(m.Id))).ToList();
        foreach (var s in _host.Mounts.Sessions.Where(s => cloud.All(k => k.Def.Id != s.Id)))
            cloud.Add((s.Definition, s));

        var drives = new List<object>();
        var cloudLetters = new HashSet<char>();
        foreach (var (def, session) in cloud)
        {
            bool isMounted = session is { State: MountState.Mounted };
            if (session != null) cloudLetters.Add(char.ToUpperInvariant(session.DriveLetter));
            string letter = session?.DriveLetter.ToString() ?? def.DriveLetter ?? "Auto";
            if (letter.Length == 1) letter += ":";
            var (total, free) = isMounted ? DiskSpace(session!.Root) : (null, null);
            var accounts = _host.AccountsBehind(def.RemoteName)
                .Select(r => _host.Accounts.GetValueOrDefault(r) ?? new CloudAccountInfo(r, null, null, null, null, null, false));

            drives.Add(new
            {
                id = def.Id,
                displayName = def.DisplayName,
                driveLetter = letter,
                root = isMounted ? session!.Root : null,
                remoteSpec = _host.AccountLabel(def.RemoteName),
                state = isMounted ? "Mounted" : session?.State.ToString() ?? "Disconnected",
                isMounted,
                canToggle = true,
                isCloud = true,
                readOnly = def.ReadOnly,
                autoMount = def.AutoMount,
                connectionType = ConnectionLabel(def.RemoteName),
                totalBytes = total,
                freeBytes = free,
                usedBytes = total - free,
                accounts = accounts.Select(AccountDto).ToList()
            });
        }

        // This PC's own disks, for browsing only (they cannot be disconnected from a phone).
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || cloudLetters.Contains(char.ToUpperInvariant(d.Name[0])) || !d.IsReady) continue;
                if (d.DriveFormat is not ("NTFS" or "exFAT" or "FAT32" or "ReFS")) continue; // leftover virtual drives are not local disks
                drives.Add(new
                {
                    id = "local_" + char.ToLowerInvariant(d.Name[0]),
                    displayName = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local disk" : d.VolumeLabel,
                    driveLetter = d.Name[..2],
                    root = d.Name,
                    remoteSpec = "This PC",
                    state = "Active",
                    isMounted = true,
                    canToggle = false,
                    isCloud = false,
                    readOnly = false,
                    autoMount = false,
                    connectionType = $"Local disk · {d.DriveFormat}",
                    totalBytes = (long?)d.TotalSize,
                    freeBytes = (long?)d.AvailableFreeSpace,
                    usedBytes = (long?)(d.TotalSize - d.AvailableFreeSpace),
                    accounts = new List<object>()
                });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.LogDebug(e, "Listing local disks failed");
        }

        await SendJsonAsync(stream, 200, "OK", drives, ct);
    }

    /// <summary>The configured drive a phone means by an id ("cloudssd") or a letter ("Z" / "Z:").</summary>
    private MountDefinition? FindDrive(string driveId)
    {
        string letter = driveId.TrimEnd(':', '\\');
        return _host.Settings.Mounts.FirstOrDefault(m => string.Equals(m.Id, driveId, StringComparison.OrdinalIgnoreCase))
            ?? _host.Settings.Mounts.FirstOrDefault(m =>
                string.Equals(_host.Mounts.Get(m.Id)?.DriveLetter.ToString() ?? m.DriveLetter, letter, StringComparison.OrdinalIgnoreCase));
    }

    private async Task HandleDriveMountEndpointAsync(string method, Dictionary<string, string> queryParams, NetworkStream stream, CancellationToken ct)
    {
        string? driveId = queryParams.GetValueOrDefault("id") ?? queryParams.GetValueOrDefault("driveId");
        if (method != "POST" || string.IsNullOrEmpty(driveId))
        {
            await SendJsonAsync(stream, 400, "Bad Request", new { success = false, error = "POST with a drive id is required." }, ct);
            return;
        }
        if (FindDrive(driveId) is not { } def)
        {
            await SendJsonAsync(stream, 404, "Not Found", new { success = false, error = $"Drive '{driveId}' is not set up on the PC." }, ct);
            return;
        }

        try
        {
            // Connecting twice would start a second rclone for the same drive letter. A drive that is already starting,
            // or that the app is restarting by itself after a failed start, gets up to a minute to come up first.
            for (int i = 0; i < 120 && _host.Mounts.Get(def.Id) is { State: MountState.Starting or MountState.Failed or MountState.Stopping }; i++)
                await Task.Delay(500, ct);
            bool already = _host.Mounts.Get(def.Id) is { State: MountState.Mounted };
            var session = already ? _host.Mounts.Get(def.Id)! : await _host.MountAsync(def, ct);
            if (!already) _host.SaveSettings();
            StatusChanged?.Invoke();
            await SendJsonAsync(stream, 200, "OK", new
            {
                success = true,
                message = already ? $"{def.DisplayName} is already connected as {session.DriveLetter}:" : $"{def.DisplayName} connected as {session.DriveLetter}:",
                id = def.Id,
                driveLetter = session.DriveLetter + ":",
                root = session.Root,
                state = session.State.ToString()
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Failed to mount drive {Id}", driveId);
            await SendJsonAsync(stream, 500, "Internal Server Error", new { success = false, error = $"Could not connect {def.DisplayName}: {ex.Message}" }, ct);
        }
    }

    private async Task HandleDriveUnmountEndpointAsync(string method, Dictionary<string, string> queryParams, NetworkStream stream, CancellationToken ct)
    {
        string? driveId = queryParams.GetValueOrDefault("id") ?? queryParams.GetValueOrDefault("driveId");
        if (method != "POST" || string.IsNullOrEmpty(driveId))
        {
            await SendJsonAsync(stream, 400, "Bad Request", new { success = false, error = "POST with a drive id is required." }, ct);
            return;
        }

        string targetId = FindDrive(driveId)?.Id ?? driveId;
        var session = _host.Mounts.Get(targetId);
        if (session is null)
        {
            await SendJsonAsync(stream, 200, "OK", new { success = true, message = "Drive is already disconnected", id = targetId }, ct);
            return;
        }
        if (_host.CurrentLaunch is { State: Games.LaunchState.Running } l && l.Game.MountId == targetId)
        {
            // The desktop app asks first; a phone tap must not pull the drive out from under a running game.
            await SendJsonAsync(stream, 409, "Conflict", new { success = false, error = $"{l.Game.Name} is running from this drive. Close the game first." }, ct);
            return;
        }

        try
        {
            string name = session.Definition.DisplayName;
            // Waits (up to a minute) for files that are still uploading, exactly like Disconnect in the desktop app.
            await _host.UnmountAsync(targetId);
            StatusChanged?.Invoke();
            await SendJsonAsync(stream, 200, "OK", new { success = true, message = $"{name} disconnected", id = targetId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Failed to unmount drive {Id}", driveId);
            await SendJsonAsync(stream, 500, "Internal Server Error", new { success = false, error = $"Could not disconnect: {ex.Message}" }, ct);
        }
    }

    private static object FileDto(FileInfo file)
    {
        string ext = file.Extension.ToLowerInvariant();
        return new
        {
            name = file.Name,
            path = CleanPath(file.FullName),
            size = file.Length,
            sizeFormatted = ByteSize.Format(file.Length),
            modified = file.LastWriteTimeUtc,
            extension = ext,
            isVideo = ext is ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".m4v",
            isAudio = ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a",
            isSave = ext is ".sav" or ".save" or ".dat" or ".bin" || file.Name.Contains("save", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesCategory(string category, string ext, string name) => category switch
    {
        "videos" => ext is ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".m4v",
        "audio" => ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a",
        "images" => ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg",
        "documents" or "docs" => ext is ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx" or ".csv" or ".md" or ".json",
        "apps" or "archives" => ext is ".apk" or ".zip" or ".rar" or ".7z" or ".iso" or ".exe" or ".msi",
        "saves" => ext is ".sav" or ".save" or ".dat" or ".bin" || name.Contains("save", StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    private async Task HandleFilesEndpointAsync(Dictionary<string, string> queryParams, NetworkStream stream, CancellationToken ct)
    {
        string? targetPath = queryParams.GetValueOrDefault("path");
        if (!string.IsNullOrEmpty(targetPath) && (targetPath = ResolveLocalPath(targetPath)) is null)
        {
            await SendJsonAsync(stream, 400, "Bad Request", new { error = "Invalid path" }, ct);
            return;
        }
        string? category = queryParams.GetValueOrDefault("category")?.ToLowerInvariant();
        string? searchQuery = queryParams.GetValueOrDefault("search")?.ToLowerInvariant();

        // No path: the uploads folder for "what did my phone send", else the first connected cloud drive.
        if (string.IsNullOrEmpty(targetPath))
        {
            if (category is "downloads" or "uploads" && Directory.Exists(UploadDirectory))
                targetPath = UploadDirectory;
            else
                targetPath = (_host.Mounts.Get(AppHost.CombinedMountId) is { State: MountState.Mounted } all
                    ? all : _host.Mounts.Sessions.FirstOrDefault(s => s.State == MountState.Mounted))?.Root;

            if (targetPath is null)
            {
                await SendJsonAsync(stream, 404, "Not Found", new { error = "No cloud drive is connected on the PC.", driveMissing = true }, ct);
                return;
            }
        }

        targetPath = NormalizePath(targetPath);

        if (!Directory.Exists(targetPath))
        {
            string root = Path.GetPathRoot(CleanPath(targetPath)) ?? "";
            bool driveMissing = root.Length > 0 && !Directory.Exists(root);
            await SendJsonAsync(stream, 404, "Not Found", new
            {
                error = driveMissing ? $"Drive {root.TrimEnd('\\')} is not connected on the PC." : "Folder not found",
                path = CleanPath(targetPath),
                driveMissing
            }, ct);
            return;
        }

        var dirInfo = new DirectoryInfo(targetPath);
        string? parent = dirInfo.Parent != null ? CleanPath(dirInfo.Parent.FullName) : null;

        // Recursive category or search filter
        if (!string.IsNullOrEmpty(category) || !string.IsNullOrEmpty(searchQuery))
        {
            var matched = new List<object>();
            bool truncated = false;
            try
            {
                var opt = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = string.IsNullOrEmpty(searchQuery) ? 2 : 3,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                };

                // A cloud drive answers every folder with a round trip, so a deep search gets a time budget.
                var clock = Stopwatch.StartNew();
                foreach (var file in dirInfo.EnumerateFiles("*", opt))
                {
                    if (clock.Elapsed > TimeSpan.FromSeconds(10) || ct.IsCancellationRequested) { truncated = true; break; }
                    if (!string.IsNullOrEmpty(category) && !MatchesCategory(category, file.Extension.ToLowerInvariant(), file.Name)) continue;
                    if (!string.IsNullOrEmpty(searchQuery) && !file.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) continue;
                    matched.Add(FileDto(file));
                    if (matched.Count >= 150) { truncated = true; break; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogDebug(ex, "Error scanning category/search files in {Path}", targetPath);
            }

            await SendJsonAsync(stream, 200, "OK", new
            {
                currentPath = CleanPath(dirInfo.FullName),
                parentPath = parent,
                folders = Array.Empty<object>(),
                files = matched,
                truncated
            }, ct);
            return;
        }

        var folders = new List<DirectoryInfo>();
        var files = new List<FileInfo>();
        try
        {
            // One pass over the folder: on a cloud drive every enumeration is a round trip to the provider.
            var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var entry in dirInfo.EnumerateFileSystemInfos("*", opt))
            {
                if (entry is DirectoryInfo sub) folders.Add(sub);
                else if (entry is FileInfo file) files.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An error is not an empty folder: tell the phone, so it does not show "Folder is empty".
            _log.LogDebug(ex, "Error reading directory {Path}", targetPath);
            await SendJsonAsync(stream, 500, "Internal Server Error", new { error = "The PC could not read this folder: " + ex.Message, path = CleanPath(targetPath) }, ct);
            return;
        }

        await SendJsonAsync(stream, 200, "OK", new
        {
            currentPath = CleanPath(dirInfo.FullName),
            parentPath = parent,
            driveRoot = Path.GetPathRoot(CleanPath(dirInfo.FullName)),
            folders = folders.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new { name = d.Name, path = CleanPath(d.FullName), modified = d.LastWriteTimeUtc }),
            files = files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(FileDto)
        }, ct);
    }

    private async Task HandleRemoteEndpointAsync(string method, Dictionary<string, string> queryParams, NetworkStream stream, CancellationToken ct)
    {
        string action = queryParams.GetValueOrDefault("action", "");
        if (method != "POST")
            await SendJsonAsync(stream, 405, "Method Not Allowed", new { success = false, error = "Use POST." }, ct);
        else if (!MediaKeys.TryPress(action))
            await SendJsonAsync(stream, 400, "Bad Request", new { success = false, error = $"Unknown remote action '{action}'." }, ct);
        else
            await SendJsonAsync(stream, 200, "OK", new { success = true, action = action.ToLowerInvariant() }, ct);
    }

    private async Task HandleStreamEndpointAsync(
        Dictionary<string, string> queryParams,
        Dictionary<string, string> headers,
        NetworkStream stream,
        string clientIp,
        CancellationToken ct)
    {
        string? filePath = ResolveLocalPath(queryParams.GetValueOrDefault("path"));
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            await SendResponseAsync(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("File not found"), ct);
            return;
        }

        var fileInfo = new FileInfo(filePath);
        long totalLength = fileInfo.Length;
        string contentType = GetMimeType(fileInfo.Extension);

        long start = 0;
        long end = totalLength - 1;
        bool isRange = false;

        if (headers.TryGetValue("Range", out var rangeHeader))
        {
            if (ParseRange(rangeHeader, totalLength) is not { } range)
            {
                await SendResponseAsync(stream, 416, "Range Not Satisfiable", "text/plain", [], ct, $"Content-Range: bytes */{totalLength}");
                return;
            }
            (start, end) = range;
            isRange = true;
        }

        long chunkLength = Math.Max(0, end - start + 1);

        var headerSb = new StringBuilder();
        if (isRange)
        {
            headerSb.Append("HTTP/1.1 206 Partial Content\r\n");
            headerSb.Append($"Content-Range: bytes {start}-{end}/{totalLength}\r\n");
        }
        else
        {
            headerSb.Append("HTTP/1.1 200 OK\r\n");
        }

        headerSb.Append($"Content-Type: {contentType}\r\n");
        headerSb.Append($"Content-Length: {chunkLength}\r\n");
        headerSb.Append("Accept-Ranges: bytes\r\n");
        headerSb.Append("Access-Control-Allow-Origin: *\r\n");
        headerSb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS, HEAD\r\n");
        headerSb.Append("Access-Control-Allow-Headers: *\r\n");
        headerSb.Append("Connection: close\r\n\r\n"); // one request per connection

        byte[] headerBytes = Encoding.UTF8.GetBytes(headerSb.ToString());
        await stream.WriteAsync(headerBytes, ct);

        // Stream file bytes with chunked rate tracking
        const int bufferSize = 128 * 1024; // 128 KB chunks
        byte[] buffer = new byte[bufferSize];
        long remaining = chunkLength;

        var sw = Stopwatch.StartNew();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize, useAsync: true);
        fs.Seek(start, SeekOrigin.Begin);

        while (remaining > 0 && !ct.IsCancellationRequested)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;

            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
            TrackBytes(read);
        }
        sw.Stop();

        long transferred = chunkLength - remaining;
        if (transferred > 0)
        {
            double elapsed = Math.Max(0.01, sw.Elapsed.TotalSeconds);
            double speedMBps = (transferred / (1024.0 * 1024.0)) / elapsed;
            string devName = ResolveDeviceName(clientIp);

            FileTransferred?.Invoke(fileInfo.Name, transferred);

            // Record streaming transfer (if initial start or substantial chunk)
            if (start == 0 || transferred > 1024 * 1024)
            {
                RecordTransfer(fileInfo.Name, fileInfo.FullName, transferred, "stream", devName, clientIp, speedMBps, remaining == 0 ? "Completed" : "Interrupted");
            }
        }
    }

    private async Task HandleDownloadEndpointAsync(
        Dictionary<string, string> queryParams,
        NetworkStream stream,
        string clientIp,
        CancellationToken ct)
    {
        string? filePath = ResolveLocalPath(queryParams.GetValueOrDefault("path"));
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            await SendResponseAsync(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("File not found"), ct);
            return;
        }

        var fileInfo = new FileInfo(filePath);
        long length = fileInfo.Length;
        string contentType = "application/octet-stream";

        var headerSb = new StringBuilder();
        headerSb.Append("HTTP/1.1 200 OK\r\n");
        headerSb.Append($"Content-Type: {contentType}\r\n");
        headerSb.Append($"Content-Length: {length}\r\n");
        headerSb.Append($"Content-Disposition: attachment; filename=\"{Uri.EscapeDataString(fileInfo.Name)}\"\r\n");
        headerSb.Append("Access-Control-Allow-Origin: *\r\n");
        headerSb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS, HEAD\r\n");
        headerSb.Append("Access-Control-Allow-Headers: *\r\n");
        headerSb.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(headerSb.ToString()), ct);

        const int bufferSize = 64 * 1024;
        byte[] buffer = new byte[bufferSize];
        long remaining = length;

        var sw = Stopwatch.StartNew();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize, useAsync: true);
        while (remaining > 0 && !ct.IsCancellationRequested)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;

            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
            TrackBytes(read);
        }
        await stream.FlushAsync(ct);
        sw.Stop();

        long transferred = length - remaining;
        double elapsed = Math.Max(0.01, sw.Elapsed.TotalSeconds);
        double speedMBps = (transferred / (1024.0 * 1024.0)) / elapsed;
        string devName = ResolveDeviceName(clientIp);

        FileTransferred?.Invoke(fileInfo.Name, transferred);
        RecordTransfer(fileInfo.Name, fileInfo.FullName, transferred, "download", devName, clientIp, speedMBps, remaining == 0 ? "Completed" : "Interrupted");
    }

    private async Task HandleUploadEndpointAsync(
        Dictionary<string, string> queryParams,
        Dictionary<string, string> headers,
        NetworkStream stream,
        string clientIp,
        CancellationToken ct)
    {
        string uploadDir = _host.Settings.MobileBridge.UploadDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cloud-SSD_Uploads");

        Directory.CreateDirectory(uploadDir);

        string filename = SafeUploadName(queryParams.GetValueOrDefault("filename"));

        long contentLength = -1;
        if (headers.TryGetValue("Content-Length", out var clStr) && !long.TryParse(clStr, out contentLength)) contentLength = -1;
        bool chunked = headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        if (contentLength < 0 && !chunked)
        {
            await SendJsonAsync(stream, 411, "Length Required", new { success = false, error = "Upload needs a Content-Length or chunked transfer encoding." }, ct);
            return;
        }

        // Keep the same 10 GB reserve the cache uses, so a phone can never fill the PC's disk.
        const long reserveBytes = 10L * 1024 * 1024 * 1024;
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(uploadDir))!).AvailableFreeSpace;
        long budget = free - reserveBytes;
        if (contentLength > budget)
        {
            await SendJsonAsync(stream, 507, "Insufficient Storage", new { success = false, error = $"Not enough free space on the PC ({ByteSize.Format(Math.Max(0, free))} free, 10 GB is kept in reserve)." }, ct);
            return;
        }

        // Never overwrite: "photo.jpg" becomes "photo (1).jpg" when it already exists.
        string destPath = Path.Combine(uploadDir, filename);
        for (int n = 1; File.Exists(destPath); n++)
            destPath = Path.Combine(uploadDir, $"{Path.GetFileNameWithoutExtension(filename)} ({n}){Path.GetExtension(filename)}");
        filename = Path.GetFileName(destPath);

        long savedSize = 0;
        bool complete;
        var sw = Stopwatch.StartNew();
        try
        {
            await using var destFile = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true);
            var body = chunked ? (Stream)new ChunkedBodyStream(stream) : stream;
            long limit = chunked ? budget : contentLength;
            byte[] buffer = new byte[64 * 1024];
            while (savedSize < limit)
            {
                int read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - savedSize)), ct);
                if (read == 0) break;
                await destFile.WriteAsync(buffer.AsMemory(0, read), ct);
                savedSize += read;
                TrackBytes(read);
            }
            complete = chunked ? ((ChunkedBodyStream)body).Finished : savedSize == contentLength;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException)
        {
            complete = false;
        }
        sw.Stop();

        double elapsed = Math.Max(0.01, sw.Elapsed.TotalSeconds);
        double speedMBps = (savedSize / (1024.0 * 1024.0)) / elapsed;
        string devName = ResolveDeviceName(clientIp);

        if (!complete)
        {
            try { File.Delete(destPath); } catch (IOException) { }
            RecordTransfer(filename, destPath, savedSize, "upload", devName, clientIp, speedMBps, "Interrupted");
            await SendJsonAsync(stream, 400, "Bad Request", new { success = false, error = "The upload was cut off or too large for the free space; nothing was saved." }, ct);
            return;
        }

        FileTransferred?.Invoke(filename, savedSize);
        RecordTransfer(filename, destPath, savedSize, "upload", devName, clientIp, speedMBps, "Completed");

        var resp = JsonSerializer.Serialize(new
        {
            success = true,
            filename,
            path = destPath,
            size = savedSize,
            speedMBps = Math.Round(speedMBps, 1)
        }, JsonOpts);

        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(resp), ct);
    }

    /// <summary>A plain file name: no folders, no ":" (NTFS alternate data streams), no reserved device names.</summary>
    internal static string SafeUploadName(string? requested)
    {
        var name = Path.GetFileName((requested ?? "").Replace('\\', '/').Split('/').Last());
        name = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '_' : c).ToArray()).Trim().TrimEnd('.');
        var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (name.Length == 0 || stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            name = $"upload_{DateTimeOffset.Now:yyyyMMdd_HHmmss}" + (name.Length > 0 ? "_" + name : ".bin");
        return name.Length > 200 ? name[..150] + Path.GetExtension(name) : name;
    }

    private async Task HandleTransfersEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        var history = GetTransferHistory();
        var json = JsonSerializer.Serialize(history, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private async Task HandleTransfersClearEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        ClearTransferHistory();
        var json = JsonSerializer.Serialize(new { success = true, message = "History cleared" }, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private string ResolveDeviceName(string clientIp)
    {
        var dev = _host.Settings.MobileBridge.PairedDevices.FirstOrDefault(d => d.IpAddress == clientIp);
        if (dev != null && !string.IsNullOrEmpty(dev.Name)) return dev.Name;
        if (IPAddress.TryParse(clientIp, out var ip) && IPAddress.IsLoopback(ip)) return "PC / Localhost";
        return "Android Phone";
    }

    private void LoadTransferHistory()
    {
        try
        {
            string path = HistoryFilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var list = JsonSerializer.Deserialize<List<TransferRecord>>(json, JsonOpts);
                if (list != null)
                {
                    lock (_historyLock)
                    {
                        _transferHistory.Clear();
                        _transferHistory.AddRange(list);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load transfer history from {Path}", HistoryFilePath);
        }
    }

    private void SaveTransferHistory()
    {
        try
        {
            string path = HistoryFilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            List<TransferRecord> copy;
            lock (_historyLock)
            {
                copy = _transferHistory.ToList();
            }
            string json = JsonSerializer.Serialize(copy, JsonOpts);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to save transfer history to {Path}", HistoryFilePath);
        }
    }

    public IReadOnlyList<TransferRecord> GetTransferHistory()
    {
        lock (_historyLock)
        {
            return _transferHistory.ToList();
        }
    }

    public void ClearTransferHistory()
    {
        lock (_historyLock)
        {
            _transferHistory.Clear();
        }
        SaveTransferHistory();
        StatusChanged?.Invoke();
    }

    public void RecordTransfer(
        string fileName,
        string? filePath,
        long bytes,
        string direction,
        string deviceName,
        string deviceIp,
        double speedMBps,
        string status = "Completed")
    {
        var record = new TransferRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            FileName = fileName,
            FilePath = filePath,
            Bytes = bytes,
            SizeFormatted = ByteSize.Format(bytes),
            Direction = direction,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Android Phone" : deviceName,
            DeviceIp = deviceIp ?? "",
            Timestamp = DateTimeOffset.Now,
            SpeedMBps = Math.Round(Math.Max(0.1, speedMBps), 1),
            Status = status
        };

        lock (_historyLock)
        {
            _transferHistory.Insert(0, record);
            if (_transferHistory.Count > 200)
            {
                _transferHistory.RemoveRange(200, _transferHistory.Count - 200);
            }
        }

        SaveTransferHistory();

        try
        {
            TransferRecorded?.Invoke(record);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error invoking TransferRecorded handler");
        }
    }

    private async Task HandleSavesEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        var saves = new List<object>();

        // Query game saves directory
        string savesDir = Path.Combine(_host.Paths.DataRoot, "Saves");
        if (Directory.Exists(savesDir))
        {
            foreach (var f in new DirectoryInfo(savesDir).EnumerateFiles("*.*", SearchOption.AllDirectories))
            {
                saves.Add(new
                {
                    name = f.Name,
                    path = f.FullName,
                    size = f.Length,
                    sizeFormatted = ByteSize.Format(f.Length),
                    modified = f.LastWriteTimeUtc
                });
            }
        }

        var json = JsonSerializer.Serialize(saves, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private async Task HandleDevicesEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        var devices = _host.Settings.MobileBridge.PairedDevices;
        var json = JsonSerializer.Serialize(devices, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private async Task HandleCleanEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        // Only caches of disconnected drives, through the same safe path as the desktop app: it refuses folders that
        // are not Cloud-SSD caches and keeps files whose changes were not uploaded yet. Connected drives are trimmed
        // by rclone itself (deleting their files underneath it could lose pending uploads).
        long freedBytes = 0;
        var notes = new List<string>();
        var cacheRoot = _host.Paths.CacheRootFor(_host.Settings);
        foreach (var m in _host.Settings.Mounts.Where(m => _host.Mounts.Get(m.Id) == null))
        {
            try
            {
                var r = Cache.CacheMaintenance.ClearMountCache(_host.Paths.MountCacheDir(_host.Settings, m.Id), cacheRoot);
                freedBytes += r.BytesFreed;
                if (!r.Cleared) notes.Add($"{m.DisplayName}: {r.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"{m.DisplayName}: {ex.Message}");
            }
        }

        var resp = JsonSerializer.Serialize(new
        {
            success = true,
            freedBytes,
            freedFormatted = ByteSize.Format(freedBytes),
            notes
        }, JsonOpts);

        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(resp), ct);
    }

    private async Task HandleStaticRouteAsync(string path, NetworkStream stream, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(path) || path == "/") path = "/index.html";
        path = path.TrimStart('/');

        // Resolve file from wwwroot/mobile
        string baseDir = Path.Combine(AppPaths.AppDirectory, "wwwroot", "mobile");
        if (!Directory.Exists(baseDir))
        {
            baseDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "mobile");
        }

        // Only files inside wwwroot\mobile: Path.Combine would happily return "C:\..." or "\\server\..." as-is.
        string? fullPath = null;
        var rel = Uri.UnescapeDataString(path).Replace('/', '\\');
        if (!rel.Contains(':') && !rel.StartsWith('\\'))
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, rel));
            if (candidate.StartsWith(Path.GetFullPath(baseDir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) fullPath = candidate;
        }

        if (fullPath != null && File.Exists(fullPath))
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(fullPath, ct);
            string mime = GetMimeType(Path.GetExtension(fullPath));
            await SendResponseAsync(stream, 200, "OK", mime, fileBytes, ct);
        }
        else
        {
            // Fallback for built-in single page app
            byte[] fallback = GetEmbeddedFallbackAsset(path);
            if (fallback.Length > 0)
            {
                string mime = GetMimeType(Path.GetExtension(path));
                await SendResponseAsync(stream, 200, "OK", mime, fallback, ct);
            }
            else
            {
                await SendResponseAsync(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("File Not Found"), ct);
            }
        }
    }

    private static byte[] GetEmbeddedFallbackAsset(string path)
    {
        if (path.Equals("index.html", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(path))
        {
            return Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>Cloud-SSD Mobile</title></head><body style=\"background:#0D1117;color:white;font-family:sans-serif;text-align:center;padding:50px;\"><h1>Cloud-SSD Mobile Hub</h1><p>Active and running. Syncing UI files...</p></body></html>");
        }
        return Array.Empty<byte>();
    }

    private async Task HandleOffloadConfigEndpointAsync(
        string method,
        Dictionary<string, string> queryParams,
        Dictionary<string, string> headers,
        NetworkStream stream,
        CancellationToken ct)
    {
        if (method == "POST")
        {
            if (queryParams.TryGetValue("enabled", out var enabledStr) && bool.TryParse(enabledStr, out var en))
            {
                _host.Settings.SmartOffload.Enabled = en;
            }
            if (queryParams.TryGetValue("thresholdGb", out var threshStr) && double.TryParse(threshStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var th) && th > 0)
            {
                _host.Settings.SmartOffload.MinFreeSpaceThresholdGB = Math.Round(th, 1);
            }
            if (queryParams.TryGetValue("targetMountId", out var targetId) && !string.IsNullOrWhiteSpace(targetId))
            {
                _host.Settings.SmartOffload.TargetMountId = targetId.Trim();
            }
            _host.SaveSettings();
        }

        var status = _host.Offloader.GetStatus();
        var json = JsonSerializer.Serialize(status, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private async Task HandleOffloadNowEndpointAsync(NetworkStream stream, CancellationToken ct)
    {
        var result = await _host.Offloader.RunOffloadAsync(manual: true, ct);
        var json = JsonSerializer.Serialize(result, JsonOpts);
        await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);
    }

    private static string GetMimeType(string ext) => ext.ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".webmanifest" => "application/manifest+json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        ".mov" => "video/quicktime",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" or ".aac" => "audio/mp4",
        ".pdf" => "application/pdf",
        ".txt" or ".log" or ".md" or ".ini" or ".cfg" or ".csv" => "text/plain; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".zip" => "application/zip",
        ".rar" => "application/vnd.rar",
        ".7z" => "application/x-7z-compressed",
        ".apk" => "application/vnd.android.package-archive",
        _ => "application/octet-stream"
    };

    /// <summary>File types the phone may open on the PC: viewers only, nothing that executes code or follows a link.</summary>
    internal static readonly HashSet<string> OpenablePcExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".tif", ".tiff",
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv",
        ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma",
        ".pdf", ".txt", ".log", ".md", ".csv", ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".rtf",
        ".zip", ".7z", ".rar",
    };

    private async Task HandleOpenPcEndpointAsync(
        Dictionary<string, string> queryParams,
        NetworkStream stream,
        CancellationToken ct)
    {
        string? filePath = ResolveLocalPath(queryParams.GetValueOrDefault("path"));
        if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath)))
        {
            var notFound = JsonSerializer.Serialize(new { success = false, error = "File not found" }, JsonOpts);
            await SendResponseAsync(stream, 404, "Not Found", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(notFound), ct);
            return;
        }
        bool isDir = Directory.Exists(filePath);
        if (!isDir && !OpenablePcExtensions.Contains(Path.GetExtension(filePath)))
        {
            // Opening a program, script or shortcut from the phone would let any paired phone run code on this PC.
            await SendJsonAsync(stream, 403, "Forbidden", new { success = false, error = "For safety, programs, scripts and shortcuts can't be opened from the phone. Media, documents and folders can." }, ct);
            return;
        }

        try
        {
            if (isDir) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { CleanPath(filePath) } });
            else Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            var ok = JsonSerializer.Serialize(new { success = true, message = $"Opened '{Path.GetFileName(filePath)}' on PC" }, JsonOpts);
            await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(ok), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to launch file on PC: {Path}", filePath);
            var err = JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOpts);
            await SendResponseAsync(stream, 500, "Internal Server Error", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(err), ct);
        }
    }

    private async Task HandlePreviewEndpointAsync(
        Dictionary<string, string> queryParams,
        NetworkStream stream,
        CancellationToken ct)
    {
        string? filePath = ResolveLocalPath(queryParams.GetValueOrDefault("path"));
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            var notFound = JsonSerializer.Serialize(new { success = false, error = "File not found" }, JsonOpts);
            await SendResponseAsync(stream, 404, "Not Found", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(notFound), ct);
            return;
        }

        try
        {
            var fi = new FileInfo(filePath);
            const int maxPreviewBytes = 256 * 1024; // 256 KB max for preview
            long readLength = Math.Min(fi.Length, maxPreviewBytes);

            byte[] buffer = new byte[readLength];
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // A cloud drive returns data in pieces, so keep reading until the buffer is full or the file ends.
                int bytesRead = await fs.ReadAtLeastAsync(buffer, (int)readLength, throwOnEndOfStream: false, ct);
                if (bytesRead < readLength)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
            }

            bool isBinary = buffer.Take(1024).Any(b => b == 0);
            string previewText = isBinary ? "" : Encoding.UTF8.GetString(buffer);

            var resp = JsonSerializer.Serialize(new
            {
                success = true,
                name = fi.Name,
                path = filePath,
                size = fi.Length,
                isBinary,
                truncated = fi.Length > maxPreviewBytes,
                content = previewText
            }, JsonOpts);

            await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(resp), ct);
        }
        catch (Exception ex)
        {
            var err = JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOpts);
            await SendResponseAsync(stream, 500, "Internal Server Error", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(err), ct);
        }
    }

    private void TrackBytes(int count)
    {
        Interlocked.Add(ref _bytesTransferred, count);
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastRateSampleTime).TotalSeconds;
        if (elapsed >= 0.5)
        {
            long delta = Interlocked.Read(ref _bytesTransferred) - _lastRateSampleBytes;
            _currentRateMbps = (delta * 8.0) / (elapsed * 1_000_000.0);
            _lastRateSampleBytes = Interlocked.Read(ref _bytesTransferred);
            _lastRateSampleTime = now;
        }
    }

    private static async Task SendResponseAsync(
        NetworkStream stream,
        int statusCode,
        string statusText,
        string contentType,
        byte[] body,
        CancellationToken ct,
        string? extraHeader = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        if (extraHeader != null) sb.Append(extraHeader).Append("\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS, HEAD\r\n");
        sb.Append("Access-Control-Allow-Headers: *\r\n");
        sb.Append("Cache-Control: no-cache, no-store, must-revalidate\r\n");
        sb.Append("Pragma: no-cache\r\n");
        sb.Append("Connection: close\r\n\r\n");

        byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes, ct);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, ct);
        }
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// Parses a single-range "bytes=" header: "a-b", "a-" or the suffix form "-n" (last n bytes). Null means
    /// unsatisfiable (416). A multi-range request is answered with its first range.
    /// </summary>
    internal static (long Start, long End)? ParseRange(string header, long length)
    {
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || length <= 0) return null;
        var spec = header[6..].Split(',')[0].Trim();
        int dash = spec.IndexOf('-');
        if (dash < 0) return null;
        var a = spec[..dash].Trim();
        var b = spec[(dash + 1)..].Trim();
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var n) || n <= 0) return null;
            return (Math.Max(0, length - n), length - 1);
        }
        if (!long.TryParse(a, out var start) || start < 0 || start >= length) return null;
        long end = length - 1;
        if (b.Length > 0)
        {
            if (!long.TryParse(b, out var e) || e < start) return null;
            end = Math.Min(e, length - 1);
        }
        return (start, end);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in pairs)
        {
            // In a query string "+" is a space (browsers' URLSearchParams and Java's URLEncoder write it that way);
            // a real plus sign arrives as %2B. Without this "Z:\My Folder" came in as "Z:\My+Folder" → 404.
            int eq = p.IndexOf('=');
            if (eq > 0)
            {
                var k = Uri.UnescapeDataString(p[..eq].Replace('+', ' '));
                var v = Uri.UnescapeDataString(p[(eq + 1)..].Replace('+', ' '));
                result[k] = v;
            }
            else
            {
                result[Uri.UnescapeDataString(p.Replace('+', ' '))] = "";
            }
        }
        return result;
    }
}

/// <summary>
/// Persistent record of a mobile-to-PC or PC-to-mobile file transfer, download or stream.
/// </summary>
public sealed class TransferRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public long Bytes { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public string Direction { get; set; } = "upload"; // "upload" (Phone -> PC), "download" (PC -> Phone), "stream"
    public string DeviceName { get; set; } = "Android Phone";
    public string DeviceIp { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public double SpeedMBps { get; set; }
    public string Status { get; set; } = "Completed"; // "Completed", "Interrupted", "Failed"
}
