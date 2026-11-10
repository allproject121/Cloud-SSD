using System.Text.Json;
using System.Text.Json.Nodes;
using CloudPlayX.Core;
using CloudPlayX.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.Mount;

public sealed record RemoteEntry(string Name, string Type);

public sealed record RemoteItem(string Name, string Path, bool IsDir, long Size, DateTimeOffset? ModTime, string? MimeType);

/// <summary>
/// Who a connected account really is and how much storage it has, asked live from the provider. Null means the
/// provider did not report it (or the login does not work right now) — never a made-up value.
/// </summary>
public sealed record CloudAccountInfo(string Remote, string? Email, string? DisplayName, long? UsedBytes, long? TotalBytes, long? FreeBytes, bool Reachable);

/// <summary>
/// Owns CLOUDPLAY X's private rclone config file (%LOCALAPPDATA%\CloudPlayX\rclone.conf). When encryption is enabled
/// the file is encrypted by rclone itself with a random password that is stored DPAPI-protected and handed to rclone
/// only through the RCLONE_CONFIG_PASS environment variable.
/// </summary>
public sealed class RcloneConfigService
{
    private readonly AppPaths _paths;
    private readonly ILogger _log;
    private const string EncryptedHeader = "# Encrypted rclone configuration File";

    public RcloneConfigService(AppPaths paths, ILogger<RcloneConfigService> log)
    {
        _paths = paths;
        _log = log;
    }

    public string ConfigPath => _paths.RcloneConfigFile;

    public bool IsEncrypted => File.Exists(ConfigPath) && File.ReadLines(ConfigPath).FirstOrDefault()?.StartsWith(EncryptedHeader) == true;

    /// <summary>Environment every rclone child process receives.</summary>
    public IReadOnlyDictionary<string, string?> BaseEnvironment()
    {
        var env = new Dictionary<string, string?>
        {
            ["RCLONE_CONFIG"] = ConfigPath,
            // Never let rclone prompt on a hidden console.
            ["RCLONE_ASK_PASSWORD"] = "false",
            ["RCLONE_PASSWORD_COMMAND"] = null,
        };
        var pass = SecretStore.Load(_paths.ConfigPasswordFile);
        env["RCLONE_CONFIG_PASS"] = IsEncrypted ? pass : null;
        return env;
    }

    public RcloneRunner CreateRunner(RcloneInfo rclone) => new(rclone.Path, BaseEnvironment());

    public async Task<IReadOnlyList<RemoteEntry>> ListRemotesAsync(RcloneInfo rclone, CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath)) return [];
        var r = await CreateRunner(rclone).RunAsync(["listremotes", "--long"], TimeSpan.FromSeconds(30), ct);
        if (!r.Success) throw new InvalidOperationException("rclone listremotes failed: " + Redactor.Redact(r.StdErr.Trim()));
        var list = new List<RemoteEntry>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            list.Add(new RemoteEntry(line[..idx], line[(idx + 1)..].Trim()));
        }
        return list;
    }

    /// <summary>Lists one directory level of a remote (used by the folder picker). Uses metadata only, no file data.</summary>
    public async Task<IReadOnlyList<RemoteItem>> ListDirectoryAsync(RcloneInfo rclone, string remote, string path, CancellationToken ct = default)
    {
        var target = $"{remote}:{path.TrimStart('/')}";
        var r = await CreateRunner(rclone).RunAsync(["lsjson", target], TimeSpan.FromMinutes(2), ct);
        if (!r.Success) throw new InvalidOperationException(ExplainError(r.StdErr));
        var arr = JsonNode.Parse(r.StdOut) as JsonArray ?? [];
        return arr.Where(n => n != null).Select(n => new RemoteItem(
            n!["Name"]!.GetValue<string>(),
            n["Path"]!.GetValue<string>(),
            n["IsDir"]?.GetValue<bool>() ?? false,
            n["Size"]?.GetValue<long>() ?? -1,
            DateTimeOffset.TryParse(n["ModTime"]?.GetValue<string>(), out var mt) ? mt : null,
            n["MimeType"]?.GetValue<string>())).ToList();
    }

    /// <summary>The whole config as JSON ("rclone config dump"). It holds logins: keep it in memory, never log it.</summary>
    public async Task<JsonObject> DumpAsync(RcloneInfo rclone, CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath)) return new JsonObject();
        var r = await CreateRunner(rclone).RunAsync(["config", "dump"], TimeSpan.FromSeconds(30), ct);
        if (!r.Success) throw new InvalidOperationException("rclone config dump failed: " + Redactor.Redact(r.StdErr.Trim()));
        return JsonNode.Parse(r.StdOut) as JsonObject ?? new JsonObject();
    }

    /// <summary>Google Drive remotes signed in through rclone's shared OAuth client, which rclone retires during 2026.</summary>
    public static IReadOnlyList<string> SharedClientDriveRemotes(JsonObject dump) =>
        dump.Where(kv => kv.Value is JsonObject o && Str(o, "type") == "drive" && Str(o, "client_id") == ""
                         && Str(o, "service_account_file") == "" && Str(o, "service_account_credentials") == "")
            .Select(kv => kv.Key).ToList();

    /// <summary>The remote plus every remote (union, combine, alias, crypt, …) that reads through it, directly or indirectly.</summary>
    public static IReadOnlySet<string> RemotesUsing(JsonObject dump, string remote)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { remote };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (name, node) in dump)
            {
                if (result.Contains(name) || node is not JsonObject o) continue;
                var targets = (Str(o, "upstreams") + " " + Str(o, "remote")).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Contains('=') ? u[(u.IndexOf('=') + 1)..] : u); // combine: "dir=remote:path"
                if (targets.Any(t => result.Any(r => t.StartsWith(r + ":", StringComparison.Ordinal))))
                {
                    result.Add(name);
                    grew = true;
                }
            }
        }
        return result;
    }

    /// <summary>Union remotes (several accounts shown as one drive) with their upstream list.</summary>
    public static IReadOnlyList<(string Name, string Upstreams)> Unions(JsonObject dump) =>
        dump.Where(kv => kv.Value is JsonObject o && Str(o, "type") == "union")
            .Select(kv => (kv.Key, Str((JsonObject)kv.Value!, "upstreams"))).ToList();

    /// <summary>
    /// Remote names in a union's or combine's upstream list ("Drive1: Drive2:/games Drive3::ro" → Drive1, Drive2,
    /// Drive3; combine entries are "folder=remote:").
    /// </summary>
    public static IReadOnlyList<string> UpstreamRemotes(string upstreams) =>
        upstreams.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Contains('=') ? u[(u.IndexOf('=') + 1)..] : u)
            .Where(u => u.IndexOf(':') > 0).Select(u => u[..u.IndexOf(':')]).ToList();

    /// <summary>Alias remotes with the remote they stand for ("gdrive:").</summary>
    public static IReadOnlyList<(string Name, string Target)> Aliases(JsonObject dump) =>
        dump.Where(kv => kv.Value is JsonObject o && Str(o, "type") == "alias")
            .Select(kv => (kv.Key, Str((JsonObject)kv.Value!, "remote"))).ToList();

    /// <summary>Combine remotes (one drive with a folder per account) with their upstream list.</summary>
    public static IReadOnlyList<(string Name, string Upstreams)> Combines(JsonObject dump) =>
        dump.Where(kv => kv.Value is JsonObject o && Str(o, "type") == "combine")
            .Select(kv => (kv.Key, Str((JsonObject)kv.Value!, "upstreams"))).ToList();

    /// <summary>Google Drive accounts signed in read-only: they can be shown but never receive files.</summary>
    public static IReadOnlyList<string> ReadOnlyDriveRemotes(JsonObject dump) =>
        dump.Where(kv => kv.Value is JsonObject o && Str(o, "type") == "drive" && Str(o, "scope") == "drive.readonly")
            .Select(kv => kv.Key).ToList();

    /// <summary>
    /// Writes a generated remote (the union or combine behind a standard drive) with exactly these options, replacing
    /// any earlier remote of that name so no stale option survives.
    /// </summary>
    public async Task WriteRemoteAsync(RcloneInfo rclone, string name, string type, IEnumerable<(string Key, string Value)> options, CancellationToken ct = default)
    {
        await CreateRunner(rclone).RunAsync(["config", "delete", name], TimeSpan.FromSeconds(30), ct);
        var args = new List<string> { "config", "create", name, type };
        foreach (var (k, v) in options) { args.Add(k); args.Add(v); }
        args.Add("--non-interactive");
        var r = await CreateRunner(rclone).RunAsync(args, TimeSpan.FromSeconds(30), ct);
        if (!r.Success) throw new InvalidOperationException(ExplainError(r.StdErr));
    }

    /// <summary>Google Drive accounts that may be written to (not signed in read-only), i.e. usable in a combined drive.</summary>
    public static IReadOnlyList<string> WritableDriveRemotes(JsonObject dump) =>
        dump.Where(kv => kv.Value is JsonObject o && Str(o, "type") == "drive" && Str(o, "scope") != "drive.readonly")
            .Select(kv => kv.Key).ToList();

    /// <summary>
    /// Adds a remote to a union so its free space joins that drive (the union's "mfs" create policy then writes new
    /// files to whichever account has the most free space). Existing files stay where they are.
    /// </summary>
    public Task AddUnionUpstreamAsync(RcloneInfo rclone, string union, string currentUpstreams, string remote, CancellationToken ct = default) =>
        SetUnionUpstreamsAsync(rclone, union, UpstreamRemotes(currentUpstreams).Append(remote), ct);

    /// <summary>Replaces a union's account list. Files stay in their own accounts; only what the drive shows changes.</summary>
    public async Task SetUnionUpstreamsAsync(RcloneInfo rclone, string union, IEnumerable<string> remotes, CancellationToken ct = default)
    {
        var upstreams = string.Join(' ', remotes.Distinct().Select(r => r + ":"));
        var r = await CreateRunner(rclone).RunAsync(["config", "update", union, "upstreams", upstreams, "--non-interactive"], TimeSpan.FromSeconds(30), ct);
        if (!r.Success) throw new InvalidOperationException(ExplainError(r.StdErr));
    }

    /// <summary>
    /// Creates a union: several accounts shown as one drive. New files go to the account with the most free space
    /// ("mfs"); changes and deletes act where the file already is ("epmfs").
    /// </summary>
    public async Task CreateUnionAsync(RcloneInfo rclone, string name, IEnumerable<string> remotes, CancellationToken ct = default)
    {
        var upstreams = string.Join(' ', remotes.Distinct().Select(r => r + ":"));
        var r = await CreateRunner(rclone).RunAsync(
            ["config", "create", name, "union", "upstreams", upstreams, "action_policy", "epmfs", "create_policy", "mfs", "search_policy", "ff", "--non-interactive"],
            TimeSpan.FromSeconds(30), ct);
        if (!r.Success) throw new InvalidOperationException(ExplainError(r.StdErr));
    }

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    public async Task DeleteRemoteAsync(RcloneInfo rclone, string name, CancellationToken ct = default)
    {
        var r = await CreateRunner(rclone).RunAsync(["config", "delete", name], TimeSpan.FromSeconds(30), ct);
        if (!r.Success) throw new InvalidOperationException(Redactor.Redact(r.StdErr));
    }

    /// <summary>
    /// Encrypts the config with a random password (stored with DPAPI) if it is not encrypted yet.
    /// rclone reads the new password through --password-command, which invokes "cpx.exe secret emit-config-pass".
    /// </summary>
    public async Task<bool> EnsureEncryptedAsync(RcloneInfo rclone, string cpxExePath, CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath) || IsEncrypted) return IsEncrypted;
        if (!File.Exists(cpxExePath))
        {
            _log.LogWarning("Cannot encrypt rclone config: helper {Helper} not found", cpxExePath);
            return false;
        }
        if (SecretStore.Load(_paths.ConfigPasswordFile) is null)
            SecretStore.Save(_paths.ConfigPasswordFile, SecretStore.RandomToken(32));
        var runner = new RcloneRunner(rclone.Path, new Dictionary<string, string?> { ["RCLONE_CONFIG"] = ConfigPath, ["RCLONE_CONFIG_PASS"] = null });
        var cmd = $"\"{cpxExePath}\" secret emit-config-pass --home \"{_paths.DataRoot}\"";
        var r = await runner.RunAsync(["config", "encryption", "set", "--password-command", cmd], TimeSpan.FromSeconds(30), ct);
        if (!r.Success || !IsEncrypted)
        {
            _log.LogWarning("rclone config encryption failed: {Err}", Redactor.Redact(r.StdErr));
            return false;
        }
        _log.LogInformation("rclone config encrypted (password protected with Windows DPAPI)");
        return true;
    }

    /// <summary>Maps common rclone/provider error text to an actionable message.</summary>
    public static string ExplainError(string stderr)
    {
        var s = Redactor.Redact(stderr);
        if (s.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) || s.Contains("token expired", StringComparison.OrdinalIgnoreCase)
            || s.Contains("couldn't fetch token", StringComparison.OrdinalIgnoreCase) || s.Contains("empty token", StringComparison.OrdinalIgnoreCase))
            return "Cloud authentication required: the saved login has expired or was revoked. Reconnect the account. (" + LastLine(s) + ")";
        if (s.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase) || s.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase) || s.Contains(" 429", StringComparison.Ordinal))
            return "The provider is rate limiting requests. rclone retries with exponential backoff; try again shortly. (" + LastLine(s) + ")";
        if (s.Contains("no such host", StringComparison.OrdinalIgnoreCase) || s.Contains("dial tcp", StringComparison.OrdinalIgnoreCase))
            return "Network unavailable: cannot reach the provider. (" + LastLine(s) + ")";
        if (s.Contains("didn't find section in config file", StringComparison.OrdinalIgnoreCase))
            return "The cloud account is not configured. Connect it in Cloud Drives.";
        if (s.Contains("directory not found", StringComparison.OrdinalIgnoreCase))
            return "Folder not found on the remote.";
        return LastLine(s);
    }

    private static string LastLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? s;
}

/// <summary>
/// Creates a Google Drive remote with the user's own OAuth client through rclone's rc config state machine.
/// The client secret travels only in the HTTP body to a 127.0.0.1 rclone daemon; the Google login happens in the
/// user's own browser on Google's official consent page (rclone listens on 127.0.0.1:53682 for the redirect).
/// </summary>
public sealed class GoogleDriveConnector
{
    private readonly RcloneConfigService _config;
    private readonly ILogger _log;

    public GoogleDriveConnector(RcloneConfigService config, ILogger<GoogleDriveConnector> log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Connection request. Leave ClientId and ClientSecret empty to use rclone's built-in shared OAuth client
    /// ("one-click" sign-in). That client is being retired by rclone during 2026 and is shared by all rclone users, so
    /// a personal client remains the reliable option.
    /// </summary>
    public sealed record Request(string RemoteName, string ClientId, string ClientSecret, bool ReadOnlyScope, string? RootFolderId)
    {
        public bool UsesSharedClient => string.IsNullOrWhiteSpace(ClientId) && string.IsNullOrWhiteSpace(ClientSecret);
    }

    public async Task ConnectAsync(RcloneInfo rclone, Request req, IProgress<string>? progress, CancellationToken ct)
    {
        if (!req.UsesSharedClient) ValidateClient(req.ClientId, req.ClientSecret);
        if (!System.Text.RegularExpressions.Regex.IsMatch(req.RemoteName, "^[A-Za-z0-9_\\-]{1,40}$"))
            throw new ArgumentException("Account name may only contain letters, digits, '-' and '_'.");

        await using var daemon = await StartDaemonAsync(rclone, ct);
        var rc = daemon.Rc;

        var parameters = new Dictionary<string, string> { ["scope"] = req.ReadOnlyScope ? "drive.readonly" : "drive" };
        if (!req.UsesSharedClient)
        {
            parameters["client_id"] = req.ClientId.Trim();
            parameters["client_secret"] = req.ClientSecret.Trim();
        }
        if (!string.IsNullOrWhiteSpace(req.RootFolderId)) parameters["root_folder_id"] = req.RootFolderId.Trim();

        progress?.Report("Saving OAuth client configuration…");
        var node = await rc.CallAsync("config/create", new
        {
            name = req.RemoteName,
            type = "drive",
            parameters,
            opt = new { nonInteractive = true, obscure = false }
        }, ct);
        await AnswerQuestionsAsync(rc, req.RemoteName, node, new()
        {
            // rclone >= 1.75 warns that its shared client is being retired; the user chose it explicitly.
            ["config_shared_client_id"] = req.UsesSharedClient ? "true" : "false",
        }, progress, ct);

        // Verify the token works with a cheap metadata call.
        progress?.Report("Verifying access to Google Drive…");
        try
        {
            var about = await rc.CallAsync("operations/about", new { fs = req.RemoteName + ":" }, ct, TimeSpan.FromMinutes(1));
            _log.LogInformation("Google Drive connected: quota total={Total} used={Used}", about["total"]?.ToString(), about["used"]?.ToString());
        }
        catch (RcException e)
        {
            // Do not leave a half-working login behind.
            try { await rc.CallAsync("config/delete", new { name = req.RemoteName }, CancellationToken.None, TimeSpan.FromSeconds(10)); } catch (Exception) { }
            throw GoogleSetupException.From(e.Message) ?? (Exception)e;
        }
    }

    /// <summary>The Google account (Gmail address) a remote is signed in as, or null when its login does not work right now.</summary>
    public async Task<string?> AccountEmailAsync(RcloneInfo rclone, string remote, CancellationToken ct)
    {
        await using var daemon = await StartDaemonAsync(rclone, ct);
        return await AccountEmailAsync(daemon.Rc, remote, ct);
    }

    /// <summary>
    /// Moves an existing Google Drive login to the user's own OAuth client: saves the new client and signs in again in
    /// the browser. When <paramref name="expectedEmail"/> is known, the new sign-in must be that same Google account,
    /// otherwise the previous login is put back — a union of several accounts must never end up pointing at the wrong
    /// one. Unmount everything that uses the remote first: a running mount would write its old token back over the new one.
    /// Returns the Google account the remote is now signed in as.
    /// </summary>
    public async Task<string?> RekeyAsync(RcloneInfo rclone, string remote, string clientId, string clientSecret, string? expectedEmail,
        IProgress<string>? progress, CancellationToken ct)
    {
        ValidateClient(clientId, clientSecret);
        Dictionary<string, string>? previous = null;
        try
        {
            await using var daemon = await StartDaemonAsync(rclone, ct);
            var rc = daemon.Rc;
            var old = await rc.CallAsync("config/get", new { name = remote }, ct);
            if (old["type"]?.GetValue<string>() != "drive") throw new InvalidOperationException($"'{remote}' is not a Google Drive account.");
            previous = new[] { "client_id", "client_secret", "token" }.ToDictionary(k => k, k => old[k]?.GetValue<string>() ?? "");

            progress?.Report($"{remote}: saving your Google key…");
            var node = await rc.CallAsync("config/update", new
            {
                name = remote,
                parameters = new Dictionary<string, string> { ["client_id"] = clientId.Trim(), ["client_secret"] = clientSecret.Trim() },
                opt = new { nonInteractive = true, obscure = false }
            }, ct);
            await AnswerQuestionsAsync(rc, remote, node, new() { ["config_refresh_token"] = "true", ["config_shared_client_id"] = "false" }, progress, ct);

            progress?.Report($"{remote}: checking which Google account you signed in with…");
            try { await rc.CallAsync("operations/about", new { fs = remote + ":" }, ct, TimeSpan.FromMinutes(1)); }
            catch (RcException e) { throw GoogleSetupException.From(e.Message) ?? (Exception)e; }
            var email = await AccountEmailAsync(rc, remote, ct);
            if (expectedEmail != null && !string.Equals(email, expectedEmail, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"You signed in as {email ?? "a different account"}, but '{remote}' belongs to {expectedEmail}. " +
                                                    $"'{remote}' was left as it was; try again and choose {expectedEmail} on Google's page.");
            _log.LogInformation("Google Drive {Remote} moved to the user's own OAuth client", remote);
            return email;
        }
        catch (Exception e) when (previous != null)
        {
            // The daemon above is stopped by now (it may have been waiting for the browser redirect); restore with a fresh one.
            _log.LogWarning("Re-keying {Remote} failed, restoring its previous login: {Error}", remote, Redactor.Redact(e.Message));
            await RestoreAsync(rclone, remote, previous);
            if (e is OperationCanceledException && !ct.IsCancellationRequested)
                throw new TimeoutException($"{remote}: Google sign-in did not finish in time; '{remote}' was left as it was.");
            throw;
        }
    }

    private async Task RestoreAsync(RcloneInfo rclone, string remote, Dictionary<string, string> previous)
    {
        try
        {
            await using var daemon = await StartDaemonAsync(rclone, CancellationToken.None);
            var node = await daemon.Rc.CallAsync("config/update", new
            {
                name = remote,
                parameters = previous,
                opt = new { nonInteractive = true, obscure = false }
            }, CancellationToken.None);
            await AnswerQuestionsAsync(daemon.Rc, remote, node, new()
            {
                ["config_refresh_token"] = "false",
                ["config_shared_client_id"] = previous["client_id"] == "" ? "true" : "false",
            }, null, CancellationToken.None, allowSignIn: false);
        }
        catch (Exception e)
        {
            _log.LogError("Could not restore the previous login of {Remote}: {Error}", remote, Redactor.Redact(e.Message));
        }
    }

    private static readonly HttpClient GoogleApi = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<string?> AccountEmailAsync(RcClient rc, string remote, CancellationToken ct)
    {
        try
        {
            // A metadata call first, so rclone refreshes (and saves) the access token if it has expired.
            await rc.CallAsync("operations/about", new { fs = remote + ":" }, ct, TimeSpan.FromMinutes(1));
            return (await IdentityAsync(rc, remote, ct)).Email;
        }
        catch (Exception e) when (e is RcException or HttpRequestException or JsonException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>Asks Google which account a Drive remote's current access token belongs to (Gmail address and name).</summary>
    private static async Task<(string? Email, string? Name)> IdentityAsync(RcClient rc, string remote, CancellationToken ct)
    {
        var cfg = await rc.CallAsync("config/get", new { name = remote }, ct);
        var token = JsonNode.Parse(cfg["token"]?.GetValue<string>() ?? "")?["access_token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) return (null, null);
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress)");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var resp = await GoogleApi.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return (null, null);
        var user = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?["user"];
        return (user?["emailAddress"]?.GetValue<string>(), user?["displayName"]?.GetValue<string>());
    }

    /// <summary>
    /// Live identity and storage of every remote: quota through rclone ("about", which for a union is the sum of its
    /// accounts) and, for Google Drive, the signed-in Gmail address and name straight from Google.
    /// </summary>
    public async Task<IReadOnlyList<CloudAccountInfo>> AccountInfoAsync(RcloneInfo rclone, IReadOnlyList<RemoteEntry> remotes, CancellationToken ct = default)
    {
        var list = new List<CloudAccountInfo>();
        if (remotes.Count == 0) return list;
        await using var daemon = await StartDaemonAsync(rclone, ct);
        foreach (var r in remotes)
        {
            try
            {
                var about = await daemon.Rc.CallAsync("operations/about", new { fs = r.Name + ":" }, ct, TimeSpan.FromSeconds(45));
                var (email, name) = r.Type == "drive" ? await IdentityAsync(daemon.Rc, r.Name, ct) : (null, null);
                list.Add(new CloudAccountInfo(r.Name, email, name, Num(about["used"]), Num(about["total"]), Num(about["free"]), true));
            }
            catch (Exception e) when (e is RcException or HttpRequestException or JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                _log.LogWarning("Account {Remote} could not be reached: {Error}", r.Name, Redactor.Redact(e.Message));
                list.Add(new CloudAccountInfo(r.Name, null, null, null, null, null, false));
            }
        }
        return list;
    }

    private static long? Num(JsonNode? n) =>
        n is JsonValue v ? v.TryGetValue<long>(out var l) ? l : v.TryGetValue<double>(out var d) ? (long)d : null : null;

    private static void ValidateClient(string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || !clientId.Trim().EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The client ID must be a Google OAuth client ID ending in .apps.googleusercontent.com.");
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new ArgumentException("The client secret is required.");
    }

    /// <summary>Walks rclone's non-interactive config questions until it is done, answering sign-in defaults.</summary>
    private async Task AnswerQuestionsAsync(RcClient rc, string remote, JsonNode node, Dictionary<string, string> answers,
        IProgress<string>? progress, CancellationToken ct, bool allowSignIn = true)
    {
        for (int step = 0; step < 10; step++)
        {
            var state = node["State"]?.GetValue<string>() ?? "";
            var err = node["Error"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException("Google Drive setup failed: " + Redactor.Redact(err));
            if (string.IsNullOrEmpty(state)) return;
            var option = node["Option"]?["Name"]?.GetValue<string>() ?? "";
            if (option == "config_is_local" && !allowSignIn) throw new InvalidOperationException("rclone asked for a new browser sign-in.");
            var answer = answers.TryGetValue(option, out var a) ? a : option switch
            {
                "config_is_local" => "true",
                "config_change_team_drive" => "false",
                _ => node["Option"]?["DefaultStr"]?.GetValue<string>() ?? ""
            };
            if (option == "config_is_local")
                progress?.Report("Opening Google's sign-in page in your browser. Complete the consent there; Cloud-SSD never sees your password.");
            else
                progress?.Report($"Finishing configuration ({option})…");
            _log.LogInformation("Drive config step {Option}", option);
            node = await rc.CallAsync("config/update", new
            {
                name = remote,
                parameters = new Dictionary<string, string>(),
                opt = new { nonInteractive = true, obscure = false, @continue = true, state, result = answer }
            }, ct, TimeSpan.FromMinutes(10));
        }
    }

    /// <summary>A private "rclone rcd" on a random loopback port with random credentials, stopped on dispose.</summary>
    private async Task<RcDaemon> StartDaemonAsync(RcloneInfo rclone, CancellationToken ct)
    {
        var port = RcClient.FindFreeLoopbackPort();
        var user = SecretStore.RandomToken(12);
        var pass = SecretStore.RandomToken(24);
        var env = new Dictionary<string, string?>(_config.BaseEnvironment()) { ["RCLONE_RC_USER"] = user, ["RCLONE_RC_PASS"] = pass };
        var psi = new RcloneRunner(rclone.Path, env).CreateStartInfo(["rcd", "--rc-addr", $"127.0.0.1:{port}"]);
        var daemon = new RcDaemon(System.Diagnostics.Process.Start(psi)!, new RcClient(port, user, pass, TimeSpan.FromMinutes(10)));
        daemon.Process.BeginOutputReadLine();
        daemon.Process.BeginErrorReadLine();
        try
        {
            for (int i = 0; i < 50 && !await daemon.Rc.PingAsync(ct); i++) await Task.Delay(100, ct);
            return daemon;
        }
        catch
        {
            await daemon.DisposeAsync();
            throw;
        }
    }

    private sealed record RcDaemon(System.Diagnostics.Process Process, RcClient Rc) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await Rc.CallAsync("core/quit", null, CancellationToken.None, TimeSpan.FromSeconds(3)); } catch (Exception) { }
            if (!Process.WaitForExit(3000)) try { Process.Kill(true); } catch (InvalidOperationException) { }
            Rc.Dispose();
            Process.Dispose();
        }
    }
}

/// <summary>A Google Cloud project setting the user must change, with a one-click link to fix it.</summary>
public sealed class GoogleSetupException(string message, string fixUrl, string fixLabel) : Exception(message)
{
    public string FixUrl { get; } = fixUrl;
    public string FixLabel { get; } = fixLabel;

    public static GoogleSetupException? From(string error)
    {
        if (error.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase) || error.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase)
            || error.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase))
        {
            var m = System.Text.RegularExpressions.Regex.Match(error, @"project[= ](\d+)");
            var url = "https://console.developers.google.com/apis/api/drive.googleapis.com/overview" + (m.Success ? "?project=" + m.Groups[1].Value : "");
            return new GoogleSetupException(
                "Almost done: the Google Drive API is switched off in your Google project. Click Enable Google Drive API, press Enable on Google's page, wait about a minute, then sign in again.",
                url, "Enable Google Drive API");
        }
        return null;
    }
}