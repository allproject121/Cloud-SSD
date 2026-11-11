using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CloudSSD.App;

/// <summary>
/// "Space &amp; RAM" for the phone: the PC's overview, folders, apps, RAM, clean-ups, large and duplicate files, and
/// its ⋮ actions (/api/space/*, switch "Space &amp; RAM"; actions also need "PC control"). The other way round the
/// phone sends its own overview here (/api/space/phone-report), fetches what the PC asked it to do
/// (/api/space/phone-commands) and answers with the result (/api/space/phone-result).
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>A phone's report (its folders and app list included) may not be bigger than this.</summary>
    internal const int MaxPhoneReportBytes = 8 * 1024 * 1024;

    private async Task<bool> TryHandleSpaceRouteAsync(string method, string path, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, CancellationToken ct)
    {
        var p = path.ToLowerInvariant();
        if (!p.StartsWith("/api/space/")) return false;
        var space = _host.Space;
        var caller = CurrentCaller.Value!;
        string deviceId = caller.Device?.Id ?? "legacy";
        switch (p)
        {
            case "/api/space/live":
            {
                // Cheap enough for the phone's Home tiles every few seconds: RAM and the Windows disk, nothing else.
                var (ramTotal, ramFree) = MemoryHealth.ReadRam();
                var sys = Path.GetPathRoot(Environment.SystemDirectory)!;
                var (diskFree, diskTotal) = CloudSSD.Core.DiskInfo.FreeSpace(sys);
                await SendJsonAsync(stream, 200, "OK", new
                {
                    at = DateTime.UtcNow, ram = new { total = ramTotal, available = ramFree },
                    disk = new { root = sys, total = diskTotal, free = diskFree }
                }, ct);
                return true;
            }
            case "/api/space/summary":
                await SendJsonAsync(stream, 200, "OK", space.Summary(), ct);
                return true;
            case "/api/space/ram":
                await SendJsonAsync(stream, 200, "OK", new { ram = space.Ram(), apps = space.RamApps() }, ct);
                return true;
            case "/api/space/folder":
            {
                var folder = await Task.Run(() => space.Folder(q.GetValueOrDefault("path") ?? "", ct: ct), ct);
                if (folder == null) await SendJsonAsync(stream, 404, "Not Found", new { error = "That folder is not on the PC's disks." }, ct);
                else await SendJsonAsync(stream, 200, "OK", folder, ct);
                return true;
            }
            case "/api/space/apps":
                await SendJsonAsync(stream, 200, "OK", await Task.Run(space.Apps, ct), ct);
                return true;
            case "/api/space/startup":
                await SendJsonAsync(stream, 200, "OK", space.Startup(), ct);
                return true;
            case "/api/space/cleanups":
                await SendJsonAsync(stream, 200, "OK", await Task.Run(() => space.Cleanups(q.GetValueOrDefault("fresh") == "1"), ct), ct);
                return true;
            case "/api/space/large":
                await SendJsonAsync(stream, 200, "OK", space.LargeFiles(), ct);
                return true;
            case "/api/space/duplicates":
            {
                var groups = space.Duplicates(q.GetValueOrDefault("fresh") == "1");
                await SendJsonAsync(stream, 200, "OK", new { ready = groups != null, groups = groups ?? [] }, ct);
                return true;
            }
            case "/api/space/action":
            {
                if (method != "POST") { await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct); return true; }
                var kind = q.GetValueOrDefault("kind") ?? "";
                var target = q.GetValueOrDefault("target") ?? "";
                var result = await space.ActionAsync(kind, target, q.GetValueOrDefault("shortcut") != "0", ct);
                await SendJsonAsync(stream, result.Ok ? 200 : 400, result.Ok ? "OK" : "Bad Request", result, ct);
                return true;
            }

            // ---- the phone's own side ----
            case "/api/space/phone-report":
            {
                if (method != "POST") { await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct); return true; }
                var body = await ReadSmallBodyAsync(stream, headers, MaxPhoneReportBytes, ct);
                if (body == null) { await SendJsonAsync(stream, 413, "Payload Too Large", new { error = "Report too big." }, ct); return true; }
                JsonObject? data;
                try { data = JsonNode.Parse(body) as JsonObject; } catch (JsonException) { data = null; }
                if (data == null) { await SendJsonAsync(stream, 400, "Bad Request", new { error = "Not a report." }, ct); return true; }
                bool allowControl = data["allowControl"]?.GetValue<bool>() == true;
                data.Remove("allowControl");
                space.Phones.SaveReport(deviceId, caller.Device?.Name ?? "Phone", allowControl, data);
                await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
                return true;
            }
            case "/api/space/phone-commands":
                await SendJsonAsync(stream, 200, "OK", space.Phones.Take(deviceId).Select(c => new { id = c.Id, kind = c.Kind, target = c.Target }), ct);
                return true;
            case "/api/space/phone-result":
            {
                if (method != "POST") { await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct); return true; }
                long.TryParse(q.GetValueOrDefault("freed"), out var freed);
                space.Phones.Complete(deviceId, q.GetValueOrDefault("id") ?? "", q.GetValueOrDefault("ok") == "1", q.GetValueOrDefault("message"), Math.Max(0, freed));
                await SendJsonAsync(stream, 200, "OK", new { ok = true }, ct);
                return true;
            }
            default:
                await SendJsonAsync(stream, 404, "Not Found", new { error = "API endpoint not found" }, ct);
                return true;
        }
    }

    /// <summary>A request body of known, limited length as text; null when it is missing or bigger than <paramref name="max"/>.</summary>
    private static async Task<string?> ReadSmallBodyAsync(NetworkStream stream, Dictionary<string, string> headers, int max, CancellationToken ct)
    {
        if (!headers.TryGetValue("Content-Length", out var cl) || !int.TryParse(cl, out var length) || length < 0) return null;
        if (length > max)
        {
            await DrainBrieflyAsync(stream, ct);
            return null;
        }
        var buf = new byte[length];
        int got = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (got < length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(got, length - got), timeout.Token);
            if (n <= 0) return null;
            got += n;
        }
        return Encoding.UTF8.GetString(buf);
    }
}
