using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudSSD.App;

namespace CloudSSD.Desktop;

/// <summary>
/// Thumbnails and photo details for the phone, made with Windows' own imaging (WIC) and, for videos, the same
/// thumbnail Explorer shows. A JPEG's built-in preview is used when it is big enough, so only the start of the
/// photo is read — on a cloud drive that is a small download instead of the whole file.
/// </summary>
internal static class MobileThumbnailer
{
    public static void Attach(LocalMobileBridgeService bridge)
    {
        bridge.ThumbnailFactory = (path, width, ct) => RunSta(() => Make(path, width), ct);
        bridge.MediaDetailsFactory = path =>
        {
            var task = RunSta(() => Details(path), CancellationToken.None);
            try { return task.Wait(TimeSpan.FromSeconds(10)) ? task.Result : null; }
            catch (AggregateException) { return null; }
        };
    }

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".3gp" };

    /// <summary>WPF imaging wants an STA thread; the bridge allows only two thumbnails at a time, so a thread each is fine.</summary>
    private static Task<T> RunSta<T>(Func<T> work, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception e) { tcs.TrySetException(e); }
        }) { IsBackground = true, Name = "Phone thumbnail" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (ct.CanBeCanceled) ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    private static byte[]? Make(string path, int width)
    {
        BitmapSource? image = null;
        if (!Videos.Contains(Path.GetExtension(path)))
        {
            try { image = Picture(path, width); }
            catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or COMException) { }
        }
        image ??= ShellThumbnail(path, width);
        if (image is null) return null;

        var encoder = new JpegBitmapEncoder { QualityLevel = 78 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource Picture(string path, int width)
    {
        int pixelWidth, pixelHeight;
        BitmapSource? embedded;
        ushort orientation = 1;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            pixelWidth = frame.PixelWidth;
            pixelHeight = frame.PixelHeight;
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("System.Photo.Orientation") && meta.GetQuery("System.Photo.Orientation") is ushort o) orientation = o;
            embedded = frame.Thumbnail is { } t && Math.Max(t.PixelWidth, t.PixelHeight) >= width / 2 ? Frozen(t) : null;
        }

        BitmapSource result = embedded ?? Decode(path, width, pixelWidth >= pixelHeight);
        double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (angle != 0) result = new TransformedBitmap(result, new RotateTransform(angle));
        // A built-in preview can still be larger than asked; scale it down so every thumbnail has the same size.
        double scale = (double)width / Math.Max(result.PixelWidth, result.PixelHeight);
        if (scale < 1) result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
        result.Freeze();
        return result;
    }

    private static BitmapSource Frozen(BitmapSource s)
    {
        var copy = new WriteableBitmap(s);
        copy.Freeze();
        return copy;
    }

    private static BitmapSource Decode(string path, int width, bool landscape)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.StreamSource = fs;
        if (landscape) bmp.DecodePixelWidth = width; else bmp.DecodePixelHeight = width;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Size in pixels, date taken and camera of a photo; null for anything Windows cannot read that from.</summary>
    private static object? Details(string path)
    {
        if (Videos.Contains(Path.GetExtension(path))) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            var meta = frame.Metadata as BitmapMetadata;
            string? taken = null, camera = null;
            try { taken = meta?.DateTaken; } catch (NotSupportedException) { }
            try { camera = string.Join(" ", new[] { meta?.CameraManufacturer, meta?.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim(); } catch (NotSupportedException) { }
            return new { width = frame.PixelWidth, height = frame.PixelHeight, dateTaken = taken, camera = string.IsNullOrEmpty(camera) ? null : camera };
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    // ---- Explorer's thumbnail (videos, and pictures WIC cannot open) ------------------------------------------------

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private const int BiggerSizeOk = 0x1, ThumbnailOnly = 0x8;

    private static BitmapSource? ShellThumbnail(string path, int width)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            if (factory.GetImage(new NativeSize { Width = width, Height = width }, BiggerSizeOk | ThumbnailOnly, out hbitmap) != 0 || hbitmap == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Explorer's bitmap has an alpha channel; a JPEG has none, so draw it on black (the video frame fills it anyway).
            var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            BitmapSource result = opaque;
            double scale = (double)width / Math.Max(result.PixelWidth, result.PixelHeight);
            if (scale < 1) result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
            result.Freeze();
            return result;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or FileNotFoundException)
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }
}
