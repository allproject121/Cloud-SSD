namespace CloudSSD.App;

// What the "Space & RAM" page shows. The PC and the phone both answer in these shapes (the phone sends the same JSON),
// so the PC page and the phone screen can show either device the same way. Sizes are bytes; times are UTC.

/// <summary>A disk (or the phone's storage): its size and what is free on it.</summary>
public sealed record SpaceVolume(string Root, string Label, long Total, long Free);

/// <summary>
/// One slice of a disk or of RAM ("Windows", "Temporary files", "Apps"). Path is where it lives, when it is one place;
/// Actions are what the ⋮ menu may offer for it (see <see cref="SpaceActionKinds"/>).
/// </summary>
public sealed record SpaceGroup(string Key, string Label, long Bytes, long Count = 0, string Note = "", string? Path = null, string[]? Actions = null);

public sealed record SpaceRam(long Total, long Available, IReadOnlyList<SpaceGroup> Groups);

/// <summary>
/// The overview of one device. Ready is false while the first full scan runs (Progress 0–1); before that the numbers
/// come from the last scan that was saved, ScannedAt says when.
/// </summary>
public sealed record SpaceSummary(string Device, string Kind, DateTime At, DateTime? ScannedAt, bool Ready, double Progress,
    IReadOnlyList<SpaceVolume> Volumes, IReadOnlyList<SpaceGroup> Groups, SpaceRam Ram);

/// <summary>One file or folder inside a folder, biggest first.</summary>
public sealed record SpaceItem(string Name, string Path, bool IsDir, long Bytes, long Files, DateTime? Modified, string Note = "",
    bool Protected = false, string[]? Actions = null);

public sealed record SpaceFolder(string Path, string Name, string? Parent, long Bytes, DateTime At, IReadOnlyList<SpaceItem> Items,
    bool Protected = false, string Note = "");

/// <summary>An installed program (PC) or app (phone).</summary>
public sealed record SpaceApp(string Id, string Name, string Publisher, long Bytes, string Version, DateTime? Installed, string[]? Actions = null);

/// <summary>An app holding RAM right now.</summary>
public sealed record SpaceRamApp(string Id, string Name, string Title, long Bytes, int Processes, string[]? Actions = null);

/// <summary>A program that starts with Windows.</summary>
public sealed record SpaceStartup(string Id, string Name, string Command, bool Enabled, string Source);

/// <summary>Something that can be cleaned in one go, with what it would free.</summary>
public sealed record SpaceCleanup(string Id, string Label, string Description, long Bytes, long Count, bool Safe, string? Path = null);

/// <summary>What an action did. FreedBytes is measured (free space before and after) where that is possible.</summary>
public sealed record SpaceResult(bool Ok, string Message, long FreedBytes = 0);

/// <summary>The ids of what the ⋮ menus can do; the same on both devices so either app can offer them for the other.</summary>
public static class SpaceActionKinds
{
    public const string Recycle = "recycle";          // to the Recycle Bin / phone trash, can be brought back
    public const string Delete = "delete";            // gone for good
    public const string Cloud = "cloud";              // move to the cloud drive (PC: leaves a shortcut)
    public const string Open = "open";                // show it in Explorer / open on the phone
    public const string Clean = "clean";              // run one cleanup
    public const string EmptyBin = "empty-bin";
    public const string Uninstall = "uninstall";
    public const string AppInfo = "app-info";         // phone: Android's App info (clear cache, force stop)
    public const string Close = "close";              // ask an app to close (it may ask to save)
    public const string Kill = "kill";                // end it at once
    public const string StartupOff = "startup-off";
    public const string StartupOn = "startup-on";
    public const string Rescan = "rescan";
}
