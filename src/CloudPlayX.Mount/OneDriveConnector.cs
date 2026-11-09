using System.Diagnostics;
using System.Text.RegularExpressions;
using CloudPlayX.Core;
using CloudPlayX.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.Mount;

/// <summary>
/// Connects Microsoft OneDrive (Personal or Business) through rclone's rc configuration state machine.
/// Uses browser OAuth consent on 127.0.0.1 redirect.
/// </summary>
public sealed class OneDriveConnector
{
    private readonly RcloneConfigService _config;
    private readonly ILogger _log;

    public OneDriveConnector(RcloneConfigService config, ILogger<OneDriveConnector> log)
    {
        _config = config;
        _log = log;
    }

    public sealed record Request(
        string RemoteName,
        string DriveType = "personal",
        string? ClientId = null,
        string? ClientSecret = null,
        bool ReadOnlyScope = false);

    public async Task ConnectAsync(RcloneInfo rclone, Request req, IProgress<string>? progress, CancellationToken ct)
    {
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
        using var rc = new RcClient(port, user, pass, TimeSpan.FromMinutes(10));
        try
        {
            for (int i = 0; i < 50 && !await rc.PingAsync(ct); i++) await Task.Delay(100, ct);

            var parameters = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(req.ClientId)) parameters["client_id"] = req.ClientId.Trim();
            if (!string.IsNullOrWhiteSpace(req.ClientSecret)) parameters["client_secret"] = req.ClientSecret.Trim();

            progress?.Report("Starting Microsoft OneDrive sign-in…");
            var node = await rc.CallAsync("config/create", new
            {
                name = req.RemoteName,
                type = "onedrive",
                parameters,
                opt = new { nonInteractive = true, obscure = false }
            }, ct);

            for (int step = 0; step < 12; step++)
            {
                var state = node["State"]?.GetValue<string>() ?? "";
                var err = node["Error"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException("OneDrive setup failed: " + Redactor.Redact(err));
                if (string.IsNullOrEmpty(state)) break;
                var option = node["Option"]?["Name"]?.GetValue<string>() ?? "";
                string answer = option switch
                {
                    "config_is_local" => "true",
                    "config_type" => req.DriveType == "business" ? "business" : "onedrive",
                    "drive_type" => req.DriveType == "business" ? "business" : "personal",
                    _ => node["Option"]?["DefaultStr"]?.GetValue<string>() ?? "0"
                };

                if (option == "config_is_local")
                    progress?.Report("Opening Microsoft's sign-in page in your browser. Complete authentication there; Cloud-SSD never sees your password.");
                else
                    progress?.Report($"Finishing OneDrive setup ({option})…");

                _log.LogInformation("OneDrive config step {Option}", option);
                node = await rc.CallAsync("config/update", new
                {
                    name = req.RemoteName,
                    parameters = new Dictionary<string, string>(),
                    opt = new { nonInteractive = true, obscure = false, @continue = true, state, result = answer }
                }, ct, TimeSpan.FromMinutes(10));
            }

            progress?.Report("Verifying access to Microsoft OneDrive…");
            try
            {
                var about = await rc.CallAsync("operations/about", new { fs = req.RemoteName + ":" }, ct, TimeSpan.FromMinutes(1));
                _log.LogInformation("OneDrive connected: quota total={Total} used={Used}", about["total"]?.ToString(), about["used"]?.ToString());
            }
            catch (Exception ex)
            {
                try { await rc.CallAsync("config/delete", new { name = req.RemoteName }, CancellationToken.None, TimeSpan.FromSeconds(5)); } catch { }
                throw new InvalidOperationException($"OneDrive verification failed: {Redactor.Redact(ex.Message)}", ex);
            }
        }
        finally
        {
            try { await rc.CallAsync("core/quit", null, CancellationToken.None, TimeSpan.FromSeconds(3)); } catch { }
            if (!proc.WaitForExit(3000)) try { proc.Kill(true); } catch { }
        }
    }
}
