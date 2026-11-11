using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CloudSSD.App;

/// <summary>
/// The phones' side of "Space &amp; RAM" on the PC. A phone sends what it knows about itself (the same shapes as
/// <see cref="SpaceSummary"/> and friends) while its app is open; the last report is kept on disk, so the PC shows it
/// with its time when the phone is away or has stopped sharing. Things the PC asks a phone to do wait here until the
/// phone picks them up, and their results come back here. A phone that does not allow control gets no commands.
/// </summary>
public sealed class PhoneSpaceStore
{
    public sealed record Report(string DeviceId, string Name, DateTime At, bool AllowControl, JsonObject Data);

    public sealed record Command(string Id, string DeviceId, string Kind, string Target, DateTime At, string State, string? Message, long FreedBytes)
    {
        public bool Finished => State is "done" or "failed";
    }

    /// <summary>A report this old means the phone app is not open; its data is "last seen", not live.</summary>
    public static readonly TimeSpan LiveFor = TimeSpan.FromSeconds(75);
    /// <summary>A command the phone did not pick up in this time is given up.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

    private readonly string _dir;
    private readonly ConcurrentDictionary<string, Report> _reports = new();
    private readonly ConcurrentDictionary<string, Command> _commands = new();

    public PhoneSpaceStore(string dir) => _dir = dir;

    /// <summary>Raised with the device id when a report or a command result arrives.</summary>
    public event Action<string>? Changed;

    private static string Safe(string id) => string.Concat(id.Where(char.IsLetterOrDigit));
    private string FileOf(string deviceId) => Path.Combine(_dir, $"phone-{Safe(deviceId)}.json");

    public void SaveReport(string deviceId, string name, bool allowControl, JsonObject data)
    {
        var r = new Report(deviceId, name, DateTime.UtcNow, allowControl, data);
        _reports[deviceId] = r;
        try
        {
            Directory.CreateDirectory(_dir);
            var json = new JsonObject { ["deviceId"] = deviceId, ["name"] = name, ["at"] = r.At, ["allowControl"] = allowControl, ["data"] = data.DeepClone() };
            File.WriteAllText(FileOf(deviceId) + ".tmp", json.ToJsonString());
            File.Move(FileOf(deviceId) + ".tmp", FileOf(deviceId), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke(deviceId);
    }

    public Report? Get(string deviceId)
    {
        if (_reports.TryGetValue(deviceId, out var r)) return r;
        try
        {
            var f = FileOf(deviceId);
            if (!File.Exists(f)) return null;
            if (JsonNode.Parse(File.ReadAllText(f)) is not JsonObject o || o["data"] is not JsonObject data) return null;
            r = new Report(deviceId, (string?)o["name"] ?? "Phone", (DateTime?)o["at"] ?? DateTime.MinValue, (bool?)o["allowControl"] ?? false, (JsonObject)data.DeepClone());
            _reports[deviceId] = r;
            return r;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>Every phone that ever reported, newest first.</summary>
    public IReadOnlyList<Report> All()
    {
        try
        {
            if (Directory.Exists(_dir))
                foreach (var f in Directory.EnumerateFiles(_dir, "phone-*.json"))
                {
                    var id = Path.GetFileNameWithoutExtension(f)["phone-".Length..];
                    if (!_reports.ContainsKey(id)) Get(id);
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return _reports.Values.OrderByDescending(r => r.At).ToList();
    }

    public static bool IsLive(Report r) => DateTime.UtcNow - r.At < LiveFor;

    /// <summary>Forgets a phone's report (it was unpaired).</summary>
    public void Forget(string deviceId)
    {
        _reports.TryRemove(deviceId, out _);
        try { File.Delete(FileOf(deviceId)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- commands ------------------------------------------------------------------------------------------------------

    /// <summary>Queues something for the phone to do; null when that phone does not allow control.</summary>
    public Command? Enqueue(string deviceId, string kind, string target)
    {
        if (Get(deviceId) is not { AllowControl: true }) return null;
        var c = new Command(Guid.NewGuid().ToString("N")[..12], deviceId, kind, target, DateTime.UtcNow, "waiting", null, 0);
        _commands[c.Id] = c;
        return c;
    }

    /// <summary>What a phone should do now (marks them as sent). Old ones that were never picked up are dropped.</summary>
    public IReadOnlyList<Command> Take(string deviceId)
    {
        foreach (var old in _commands.Values.Where(c => DateTime.UtcNow - c.At > CommandTimeout).ToList())
            if (!old.Finished) _commands[old.Id] = old with { State = "failed", Message = "The phone did not pick it up in time." };
        var list = _commands.Values.Where(c => c.DeviceId == deviceId && c.State == "waiting").OrderBy(c => c.At).ToList();
        foreach (var c in list) _commands[c.Id] = c with { State = "sent" };
        return list;
    }

    public void Complete(string deviceId, string id, bool ok, string? message, long freed)
    {
        if (!_commands.TryGetValue(id, out var c) || c.DeviceId != deviceId) return;
        _commands[id] = c with { State = ok ? "done" : "failed", Message = message, FreedBytes = freed };
        Changed?.Invoke(deviceId);
    }

    public Command? Status(string id) => _commands.TryGetValue(id, out var c) ? c : null;

    public IReadOnlyList<Command> Recent(string deviceId) =>
        _commands.Values.Where(c => c.DeviceId == deviceId).OrderByDescending(c => c.At).Take(20).ToList();
}
