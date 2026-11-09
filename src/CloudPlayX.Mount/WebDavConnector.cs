using System.Diagnostics;
using System.Text.RegularExpressions;
using CloudPlayX.Core;
using CloudPlayX.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.Mount;

/// <summary>
/// Connects standard WebDAV / Nextcloud / ownCloud endpoints through rclone's rc configuration.
/// Supports username + password or application tokens.
/// </summary>
public sealed class WebDavConnector
{
    private readonly RcloneConfigService _config;
    private readonly ILogger _log;

    public WebDavConnector(RcloneConfigService config, ILogger<WebDavConnector> log)
    {
        _config = config;
        _log = log;
    }

    public sealed record Request(
        string RemoteName,
        string Url,
        string Vendor,
        string Username,
        string Password,
        bool ReadOnlyScope = false);

    public async Task ConnectAsync(RcloneInfo rclone, Request req, IProgress<string>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Url) || !Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Please provide a valid WebDAV URL starting with http:// or https://");
        if (string.IsNullOrWhiteSpace(req.Username))
            throw new ArgumentException("Username is required.");
        if (!Regex.IsMatch(req.RemoteName, "^[A-Za-z0-9_\\-]{1,40}$"))
            throw new ArgumentException("Account name may only contain letters, digits, '-' and '_'.");

        var port = RcClient.FindFreeLoopbackPort();
        var user = SecretStore.RandomToken(12);
        var pass = SecretStore.RandomToken(24);
        var env = new Dictionary<string, string?>(_config.BaseEnvironment()) { ["RCLONE_RC_USER"] = user, ["RCLONE_RC_PASS"] = pass };
        var runner = new RcloneRunner(rclone.Path, env);
        var psi = runner.CreateStartInfo(["rcd", "--rc-addr", $"127.0.0.1:{port}"]);
        using var proc = Process.Start(psi)!;
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        using var rc = new RcClient(port, user, pass, TimeSpan.FromMinutes(2));
        try
        {
            for (int i = 0; i < 50 && !await rc.PingAsync(ct); i++) await Task.Delay(100, ct);

            progress?.Report("Configuring WebDAV connection…");
            var vendor = string.IsNullOrWhiteSpace(req.Vendor) ? "other" : req.Vendor.Trim().ToLowerInvariant();
            var parameters = new Dictionary<string, string>
            {
                ["url"] = req.Url.Trim(),
                ["vendor"] = vendor,
                ["user"] = req.Username.Trim(),
                ["pass"] = req.Password
            };

            await rc.CallAsync("config/create", new
            {
                name = req.RemoteName,
                type = "webdav",
                parameters,
                opt = new { nonInteractive = true, obscure = true }
            }, ct);

            progress?.Report("Verifying WebDAV server access…");
            try
            {
                var list = await rc.CallAsync("operations/list", new { fs = req.RemoteName + ":", opt = new { recurse = false } }, ct, TimeSpan.FromSeconds(30));
                _log.LogInformation("WebDAV connected successfully to {Url}", req.Url);
            }
            catch (Exception ex)
            {
                try { await rc.CallAsync("config/delete", new { name = req.RemoteName }, CancellationToken.None, TimeSpan.FromSeconds(5)); } catch { }
                throw new InvalidOperationException($"WebDAV verification failed: {Redactor.Redact(ex.Message)}", ex);
            }
        }
        finally
        {
            try { await rc.CallAsync("core/quit", null, CancellationToken.None, TimeSpan.FromSeconds(3)); } catch { }
            if (!proc.WaitForExit(3000)) try { proc.Kill(true); } catch { }
        }
    }
}
