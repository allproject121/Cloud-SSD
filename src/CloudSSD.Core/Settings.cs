using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudSSD.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public CacheSettings Cache { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public StreamingSettings Streaming { get; set; } = new();
    public List<MountDefinition> Mounts { get; set; } = new();
    /// <summary>Last known sign-in address of each account (remote name → e-mail), so names stay stable while offline.</summary>
    public Dictionary<string, string> AccountEmails { get; set; } = new();
    public string? RclonePathOverride { get; set; }
    public bool RestoreMountsOnStartup { get; set; } = true;
    public bool KeepMountsAfterExit { get; set; } = false;
    public bool EncryptRcloneConfig { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public string? LocalGamesRoot { get; set; }
    public MobileBridgeSettings MobileBridge { get; set; } = new();
    public SmartOffloadSettings SmartOffload { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
}

public sealed class CacheSettings
{
    /// <summary>Root directory for rclone VFS caches. Each mount gets its own subdirectory.</summary>
    public string? CacheRoot { get; set; }

    /// <summary>Total physical disk budget for all mount caches.</summary>
    public long BudgetBytes { get; set; } = 20 * ByteSize.GiB;

    /// <summary>Free space that must always remain on the cache volume for Windows.</summary>
    public long MinFreeSpaceBytes { get; set; } = 8 * ByteSize.GiB;

    /// <summary>How long unused cached data may stay before rclone evicts it.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// rclone --buffer-size. In full cache mode rclone reads buffer-size + read-ahead past every read position.
    /// Measured: default 16M causes large speculative downloads for random game reads, so the default here is 0 and
    /// the prediction engine owns read-ahead.
    /// </summary>
    public long BufferSizeBytes { get; set; } = 0;

    public long ReadAheadBytes { get; set; } = 0;

    public long ReadChunkSizeBytes { get; set; } = 4 * ByteSize.MiB;

    public int ReadChunkStreams { get; set; } = 0;

    public TimeSpan WriteBack { get; set; } = TimeSpan.FromSeconds(5);

    public List<string> PinnedPaths { get; set; } = new();
}

public sealed class NetworkSettings
{
    /// <summary>User-declared internet speed in megabits per second (used until a measurement exists).</summary>
    public double DeclaredDownloadMbps { get; set; } = 70;

    /// <summary>Last measured throughput (Mbps), null if never measured.</summary>
    public double? MeasuredDownloadMbps { get; set; }
    public double? MeasuredLatencyMs { get; set; }
    public DateTimeOffset? MeasuredAt { get; set; }

    /// <summary>Maximum share of bandwidth background (P2/P3) prefetch may use.</summary>
    public double BackgroundShare { get; set; } = 0.6;

    /// <summary>Optional hard rclone --bwlimit in Mbps (0 = unlimited).</summary>
    public double HardLimitMbps { get; set; } = 0;

    /// <summary>
    /// While a game or a phone stream is in front, cloud uploads are slowed to this many Mbps so they do not take
    /// the internet the game needs (0 = never slow them). Downloads keep their speed: a game may be loading from the cloud.
    /// </summary>
    public double GameUploadLimitMbps { get; set; } = 1;

    public int MaxPrefetchConcurrency { get; set; } = 4;

    /// <summary>rclone --tpslimit for provider API calls (0 = unlimited). Protects Google Drive quotas.</summary>
    public double TpsLimit { get; set; } = 10;

    public double EffectiveMbps => MeasuredDownloadMbps ?? DeclaredDownloadMbps;
}

public sealed class StreamingSettings
{
    public bool PredictionEnabled { get; set; } = true;

    /// <summary>Record per-read telemetry from rclone debug logs. Costs some CPU while active.</summary>
    public bool ReadProfilingEnabled { get; set; } = true;

    public long BlockSizeBytes { get; set; } = ByteSize.MiB;

    public double MinConfidence { get; set; } = 0.4;

    /// <summary>Upper bound of bytes the engine may prefetch speculatively in one game session.</summary>
    public long SessionPrefetchBudgetBytes { get; set; } = 4 * ByteSize.GiB;

    public TimeSpan StartupWindow { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>How far ahead (in game-progress time) learned startup data is released for prefetch during a launch.</summary>
    public TimeSpan StartupHorizon { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Predictors with accuracy below this after enough samples are disabled automatically.</summary>
    public double MinPredictorAccuracy { get; set; } = 0.25;

    public int PredictorEvaluationSamples { get; set; } = 64;
}

public sealed class MountDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; set; } = "Cloud Drive";

    /// <summary>rclone remote name without the trailing colon, e.g. "gdrive".</summary>
    public string RemoteName { get; set; } = "";

    /// <summary>Optional path inside the remote, e.g. "Games".</summary>
    public string RemotePath { get; set; } = "";

    /// <summary>Preferred drive letter, e.g. "X". Null = pick automatically.</summary>
    public string? DriveLetter { get; set; }

    public bool ReadOnly { get; set; }
    public bool AutoMount { get; set; } = true;

    public Dictionary<string, string> ExtraFlags { get; set; } = new();

    public string RemoteSpec => string.IsNullOrEmpty(RemotePath) ? $"{RemoteName}:" : $"{RemoteName}:{RemotePath}";
}

public sealed class MobileBridgeSettings
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 8090;
    public string PairingToken { get; set; } = NewPairingToken();

    /// <summary>Tokens shorter than this (the 12-character ones from v0.7.0) are replaced on start; phones re-pair by QR.</summary>
    public const int MinPairingTokenLength = 32;

    /// <summary>128 random bits as 32 lowercase hex characters.</summary>
    public static string NewPairingToken() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public string? UploadDirectory { get; set; }
    public string? PhotoBackupDirectory { get; set; }
    public bool AllowDriveBrowsing { get; set; } = true;
    public bool AllowStreaming { get; set; } = true;
    public bool AllowUploads { get; set; } = true;
    public bool AllowGamingHud { get; set; } = true;
    /// <summary>Lets a paired phone create, change and delete files on the cloud drives (and back up its camera folder).</summary>
    public bool AllowFileManagement { get; set; } = true;
    /// <summary>Lets a paired phone act on the PC: connect/disconnect drives, free up space, open files on its screen, restore game saves.</summary>
    public bool AllowPcControl { get; set; } = true;
    /// <summary>Lets a paired phone browse and download from this PC's own disks (C:, D:…), not only the cloud drives. Off by default.</summary>
    public bool AllowPcDisks { get; set; }
    /// <summary>Lets a paired phone reach the PC through Tailscale (away from home). Pairing itself still needs the home network.</summary>
    public bool AllowTailscale { get; set; }
    /// <summary>Refuses connections that come neither from the local network nor through Tailscale.</summary>
    public bool LocalAndTailnetOnly { get; set; } = true;
    /// <summary>When Tailscale is signed in on this PC, turn <see cref="AllowTailscale"/> on by itself (the user asked for it to be automatic).</summary>
    public bool TailscaleAuto { get; set; } = true;
    /// <summary>The firewall rule for Tailscale was asked for once (Windows shows a Yes/No prompt); never asked again by itself.</summary>
    public bool TailscaleFirewallAsked { get; set; }
    /// <summary>Lets a paired phone see where the PC's space and RAM go (Space &amp; RAM). Changing anything there also needs <see cref="AllowPcControl"/>.</summary>
    public bool AllowSpace { get; set; } = true;

    /// <summary>
    /// Whether the shared <see cref="PairingToken"/> still opens the bridge. Phones paired before device tokens keep
    /// working with it until it is turned off in Mobile Hub; null = not decided yet (decided once on start).
    /// </summary>
    public bool? LegacyTokenAccepted { get; set; }

    /// <summary>Key for signed, short-lived file links that can be handed to other apps without the token.</summary>
    public string MediaKey { get; set; } = "";

    /// <summary>A public random id for this PC, so a phone can tell it is talking to its own PC before it sends its token.</summary>
    public string PcId { get; set; } = "";

    public List<PairedDevice> PairedDevices { get; set; } = new();

    /// <summary>
    /// Lets a paired phone play the game running on this PC: the phone sees the screen and hears the game, and its
    /// touches reach the emulator. Only on the home network, only with a per-phone key, only while an emulator is in front.
    /// </summary>
    public bool AllowGameStream { get; set; } = true;

    public GameStreamSettings GameStream { get; set; } = new();
}

/// <summary>Picture, sound and game start for "Play on phone".</summary>
public sealed class GameStreamSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 60;
    /// <summary>Starting bitrate; lowered by itself while the Wi-Fi cannot keep up and raised again up to this.</summary>
    public int BitrateKbps { get; set; } = 10_000;
    public int MinBitrateKbps { get; set; } = 2_000;
    /// <summary>Game sound goes to the phone. Only the emulator's own sound when Windows allows it, else everything the PC plays.</summary>
    public bool Sound { get; set; } = true;
    /// <summary>What "Play" starts on the PC; the same command Sunshine used for Free Fire.</summary>
    public string GameName { get; set; } = "Free Fire";
    public string LaunchPath { get; set; } = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe";
    public string LaunchArguments { get; set; } = "--instance Nougat32 --cmd launchApp --package \"com.garena.game.ffi\"";
}

public sealed class PairedDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Android Device";
    /// <summary>The address the phone last used (it changes with the network); shown only, never used to let it in.</summary>
    public string IpAddress { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.Now;
    public bool IsBlocked { get; set; }
    /// <summary>SHA-256 (hex) of this phone's own token. The token itself is never stored on the PC.</summary>
    public string? TokenHash { get; set; }
    /// <summary>A random id the phone app keeps for its install, so re-pairing updates this entry instead of adding one.</summary>
    public string? InstallId { get; set; }
}

public sealed class SmartOffloadSettings
{
    public bool Enabled { get; set; } = true;
    public double MinFreeSpaceThresholdGB { get; set; } = 50.0;
    public string TargetMountId { get; set; } = "cloudssd15tb";
    public string TargetSubfolder { get; set; } = "Offloaded_Files";
    public bool OffloadUploadsFolder { get; set; } = true;
    /// <summary>
    /// Also move old big files that lie directly in the Downloads folder (never its subfolders). Off unless the
    /// user turns it on, because it takes the user's own files off the PC.
    /// </summary>
    public bool OffloadDownloadsFolder { get; set; }
    /// <summary>Below the limit, first delete temporary files nobody has touched for a week (what Windows' Disk Cleanup removes).</summary>
    public bool CleanTempFiles { get; set; } = true;
    /// <summary>
    /// Below the limit, also remove Recycle Bin items deleted more than 30 days ago. Off unless the user turns it
    /// on: these were the user's own files.
    /// </summary>
    public bool CleanRecycleBin { get; set; }
    public bool EvictVfsCacheOnLowSpace { get; set; } = true;
    public DateTimeOffset? LastOffloadTime { get; set; }
    public long TotalBytesOffloaded { get; set; }
}

public sealed class MemorySettings
{
    /// <summary>PC Memory auto mode: when RAM runs low, idle background apps are moved out of RAM.</summary>
    public bool AutoRelief { get; set; } = true;

    /// <summary>Game mode: when a game or a phone stream starts, RAM is freed at once and the apps in <see cref="CloseForGame"/> are asked to close.</summary>
    public bool GameMode { get; set; } = true;

    /// <summary>Process names (no ".exe") of the apps the user chose to have closed while a game runs. Empty until the user picks some.</summary>
    public List<string> CloseForGame { get; set; } = new();

    /// <summary>Open the apps game mode closed again when the game is over.</summary>
    public bool ReopenAfterGame { get; set; } = true;
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object _gate = new();

    public JsonSettingsStore(string path) => Path = path;
    public string Path { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(Path)) return new AppSettings();
            try
            {
                var json = File.ReadAllText(Path);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
            catch (JsonException)
            {
                // Keep the damaged file for diagnostics and start from defaults.
                File.Copy(Path, Path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }
    }
}
