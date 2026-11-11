namespace CloudSSD.Providers;

public enum SupportLevel
{
    /// <summary>Implemented and exercised by Cloud-SSD's own workflow (connect, mount, stream).</summary>
    Supported,
    /// <summary>Works through the corresponding rclone backend; not tested end-to-end by Cloud-SSD.</summary>
    AvailableUntested,
    /// <summary>No authorized integration exists; the adapter is a placeholder that explains why.</summary>
    Unsupported
}

public sealed record ProviderCapabilities(bool RangeReads, bool Checksums, bool Writes, bool ChangeNotifications, string? Notes = null);

/// <summary>Describes one cloud backend and how Cloud-SSD connects to it.</summary>
public interface ICloudProvider
{
    string Id { get; }
    string DisplayName { get; }
    string? RcloneBackend { get; }
    SupportLevel Support { get; }
    ProviderCapabilities Capabilities { get; }
    bool RequiresOAuthClient { get; }
    string SetupSummary { get; }
    string? DocumentationUrl { get; }
}

public sealed record CloudProvider(
    string Id, string DisplayName, string? RcloneBackend, SupportLevel Support, ProviderCapabilities Capabilities,
    bool RequiresOAuthClient, string SetupSummary, string? DocumentationUrl) : ICloudProvider;

public static class ProviderRegistry
{
    public static readonly CloudProvider GoogleDrive = new(
        "gdrive", "Google Drive", "drive", SupportLevel.Supported,
        new ProviderCapabilities(RangeReads: true, Checksums: true, Writes: true, ChangeNotifications: true,
            "MD5 checksums and modification times are used for cache validation; Drive API quotas apply (rate limits are retried with backoff by rclone)."),
        RequiresOAuthClient: true,
        "Sign in directly in your browser. Uses your own Google OAuth client or Cloud-SSD's direct connection.",
        "https://rclone.org/drive/#making-your-own-client-id");

    public static readonly CloudProvider OneDrive = new(
        "onedrive", "Microsoft OneDrive", "onedrive", SupportLevel.Supported,
        new ProviderCapabilities(RangeReads: true, Checksums: true, Writes: true, ChangeNotifications: true,
            "SHA1/QuickXor checksums and modification times; supports Personal OneDrive and OneDrive for Business."),
        RequiresOAuthClient: false,
        "Sign in directly in your browser with your Microsoft account (Personal or Business).",
        "https://rclone.org/onedrive/");

    public static readonly CloudProvider WebDav = new(
        "webdav", "WebDAV / Nextcloud", "webdav", SupportLevel.Supported,
        new ProviderCapabilities(RangeReads: true, Checksums: true, Writes: true, ChangeNotifications: false,
            "Standard HTTP/HTTPS WebDAV; supported by Nextcloud, ownCloud, and Seedboxes."),
        RequiresOAuthClient: false,
        "Connect with your WebDAV server URL, username, and password or app token.",
        "https://rclone.org/webdav/");

    public static readonly CloudProvider TeraBox = new(
        "terabox", "TeraBox", null, SupportLevel.Unsupported,
        new ProviderCapabilities(false, false, false, false,
            "No official rclone backend. The community fork that exists authenticates with browser session cookies against undocumented web endpoints, " +
            "which Cloud-SSD will not use. Revisit when TeraBox publishes a public API with OAuth and byte-range downloads."),
        RequiresOAuthClient: false,
        "Not available. See docs/TERABOX_RESEARCH.md.", null);

    public static IReadOnlyList<CloudProvider> All { get; } =
    [
        GoogleDrive,
        OneDrive,
        WebDav,
        new("dropbox", "Dropbox", "dropbox", SupportLevel.AvailableUntested,
            new ProviderCapabilities(true, true, true, true), true,
            "Configure with 'rclone config' (backend 'dropbox'); a custom app key is recommended.", "https://rclone.org/dropbox/"),
        new("s3", "S3-compatible storage", "s3", SupportLevel.AvailableUntested,
            new ProviderCapabilities(true, true, true, false), false,
            "Configure with 'rclone config' (backend 's3'); access keys stay in the encrypted rclone config.", "https://rclone.org/s3/"),
        new("nas", "Local NAS (SMB/SFTP)", "smb", SupportLevel.AvailableUntested,
            new ProviderCapabilities(true, false, true, false), false,
            "Configure with 'rclone config' (backend 'smb' or 'sftp').", "https://rclone.org/smb/"),
        TeraBox,
    ];

    public static CloudProvider? ForBackend(string rcloneType) =>
        All.FirstOrDefault(p => string.Equals(p.RcloneBackend, rcloneType, StringComparison.OrdinalIgnoreCase));
}
