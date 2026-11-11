using System.Runtime.InteropServices;

namespace CloudSSD.App;

/// <summary>Writes Windows shortcut (.lnk) files through the shell's own scripting object.</summary>
public static class ShellShortcut
{
    /// <summary>
    /// Creates or replaces the shortcut at <paramref name="linkPath"/>. The target need not exist yet. False when
    /// Windows refuses; a shortcut is a convenience, so callers carry on without it.
    /// </summary>
    public static bool TryCreate(string linkPath, string targetPath, string description)
    {
        try
        {
            if (Type.GetTypeFromProgID("WScript.Shell") is not { } type || Activator.CreateInstance(type) is not { } shell) return false;
            try
            {
                dynamic link = ((dynamic)shell).CreateShortcut(linkPath);
                try
                {
                    link.TargetPath = targetPath;
                    link.Description = description;
                    link.Save();
                }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
            return File.Exists(linkPath);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or IOException or UnauthorizedAccessException
                                      or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return false;
        }
    }
}
