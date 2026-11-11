using System.Runtime.InteropServices;

namespace CloudSSD.App;

/// <summary>
/// The phone's PC remote: presses one of the keyboard's media keys (volume and playback). Nothing here can type text
/// or start a program, so a paired phone gains no more than a multimedia keyboard has.
/// </summary>
internal static class MediaKeys
{
    private static readonly Dictionary<string, byte> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["volup"] = 0xAF,      // VK_VOLUME_UP
        ["voldown"] = 0xAE,    // VK_VOLUME_DOWN
        ["mute"] = 0xAD,       // VK_VOLUME_MUTE
        ["playpause"] = 0xB3,  // VK_MEDIA_PLAY_PAUSE
        ["next"] = 0xB0,       // VK_MEDIA_NEXT_TRACK
        ["prev"] = 0xB1,       // VK_MEDIA_PREV_TRACK
    };

    public static bool IsKnown(string action) => Keys.ContainsKey(action);

    /// <summary>False for an action that is not a media key; nothing is pressed then.</summary>
    public static bool TryPress(string action)
    {
        if (!Keys.TryGetValue(action, out var vk)) return false;
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KeyUp, UIntPtr.Zero);
        return true;
    }

    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
}
