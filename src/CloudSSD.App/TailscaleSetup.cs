using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// Sets up "away from home" by itself: installs Tailscale (winget), opens its sign-in in the browser, turns the
/// phone bridge's Tailscale access on once Tailscale runs, and adds the firewall rule that lets phones reach the bridge
/// through it (Windows asks once with a Yes/No prompt). Only the sign-in needs the user.
/// </summary>
public static class TailscaleSetup
{
    public enum State { NotInstalled, Stopped, NeedsLogin, Running }

    public const string FirewallRuleName = "Cloud-SSD via Tailscale";

    /// <summary>tailscale.exe, where the installer puts it or on PATH; null when Tailscale is not installed.</summary>
    public static string? FindExe()
    {
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var p = Path.Combine(dir, "Tailscale", "tailscale.exe");
            if (File.Exists(p)) return p;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), "tailscale.exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static State Read()
    {
        var exe = FindExe();
        if (exe == null) return State.NotInstalled;
        var (code, output) = Run(exe, "status --json", TimeSpan.FromSeconds(6));
        return code == null ? State.Stopped : Parse(output);
    }

    /// <summary>The state from "tailscale status --json" (BackendState); anything unreadable counts as stopped.</summary>
    internal static State Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var s = doc.RootElement.TryGetProperty("BackendState", out var b) ? b.GetString() : null;
            return s switch
            {
                "Running" => State.Running,
                "NeedsLogin" or "NeedsMachineAuth" => State.NeedsLogin,
                _ => State.Stopped
            };
        }
        catch (JsonException) { return State.Stopped; }
    }

    /// <summary>Installs Tailscale with winget (its installer asks for admin once). Message says what happened.</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(CancellationToken ct = default)
    {
        if (FindExe() != null) return (true, "Tailscale is already installed.");
        var (code, output) = await Task.Run(() => Run("winget",
            "install --id Tailscale.Tailscale -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", TimeSpan.FromMinutes(15)), ct);
        if (code == null && output == "")
        {
            // No winget on this PC: the download page does the same.
            Process.Start(new ProcessStartInfo("https://tailscale.com/download/windows") { UseShellExecute = true });
            return (false, "Windows has no winget here, so the Tailscale download page is open. Install it from there, then press the button again.");
        }
        for (int i = 0; i < 20 && FindExe() == null; i++) await Task.Delay(1000, ct);
        return FindExe() != null ? (true, "Tailscale is installed.") : (false, "Tailscale could not be installed: " + LastLine(output));
    }

    /// <summary>Starts Tailscale's sign-in: the login page opens in the browser. Then waits (up to 5 minutes) until it runs.</summary>
    public static async Task<bool> LoginAsync(CancellationToken ct = default)
    {
        var exe = FindExe();
        if (exe == null) return false;
        if (Read() == State.Running) return true;
        var psi = new ProcessStartInfo(exe, "up")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return false;
        bool opened = false;
        void Look(string? line)
        {
            if (opened || line == null) return;
            var m = Regex.Match(line, @"https://login\.tailscale\.com/\S+");
            if (!m.Success) return;
            opened = true;
            Process.Start(new ProcessStartInfo(m.Value) { UseShellExecute = true });
        }
        p.OutputDataReceived += (_, e) => Look(e.Data);
        p.ErrorDataReceived += (_, e) => Look(e.Data);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var until = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            if (Read() == State.Running) break;
        }
        try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
        TailscaleInfo.Forget();
        return Read() == State.Running;
    }

    public static bool FirewallRuleExists()
    {
        var (code, output) = Run("netsh", $"advfirewall firewall show rule name=\"{FirewallRuleName}\"", TimeSpan.FromSeconds(10));
        return code == 0 && output.Contains(FirewallRuleName, StringComparison.OrdinalIgnoreCase);
    }

    public static string FirewallArguments(int port) =>
        $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} remoteip=100.64.0.0/10 profile=any";

    /// <summary>Adds the firewall rule as administrator (Windows shows its Yes/No prompt). False when the user said No.</summary>
    public static bool AddFirewallRule(int port)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", FirewallArguments(port)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p == null) return false;
            p.WaitForExit(30000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // "No" in the prompt
    }

    /// <summary>
    /// The automatic part, run every minute by the desktop app: once Tailscale runs here, phones may use it.
    /// True when the setting changed. The firewall prompt is left to the caller (it shows a note first).
    /// </summary>
    public static bool AutoEnable(MobileBridgeSettings bridge)
    {
        if (!bridge.TailscaleAuto || bridge.AllowTailscale) return false;
        if (Read() != State.Running) return false;
        bridge.AllowTailscale = true;
        TailscaleInfo.Forget();
        return true;
    }

    private static string LastLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "unknown error";

    /// <summary>Runs a program hidden and returns its exit code (null when it could not start or timed out) and output.</summary>
    private static (int? Code, string Output) Run(string exe, string args, TimeSpan timeout)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            });
            if (p == null) return (null, "");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                return (null, "timed out");
            }
            return (p.ExitCode, stdout.Result + stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (null, ""); }
    }
}
