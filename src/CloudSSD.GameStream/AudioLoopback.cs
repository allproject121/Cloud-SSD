using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CloudSSD.GameStream;

/// <summary>
/// Records what the game plays, as 16-bit stereo 48 kHz, in small pieces so the phone hears it with the picture.
/// Prefers Windows' per-app loopback (only the emulator and its child processes, Windows 10 2004 and later); when that
/// is not possible it records everything the PC plays.
/// </summary>
public sealed class AudioLoopback : IDisposable
{
    public const int SampleRate = 48_000, Channels = 2, BytesPerFrame = 4;

    private readonly ILogger _log;
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private bool _float;   // the system mix (fallback) may only come as 32-bit float

    /// <summary>"game only" or "whole PC".</summary>
    public string Source { get; private set; } = "";

    public AudioLoopback(int? processId, ILogger log)
    {
        _log = log;
        if (processId is int pid)
        {
            try
            {
                _client = ActivateForProcess(pid);
                InitClient(_client, Pcm16(), allowFloat: false);
                Source = "game only";
            }
            catch (Exception ex) when (ex is COMException or TimeoutException or InvalidCastException)
            {
                _log.LogInformation(ex, "Per-app sound capture not available, recording the whole PC instead");
                ReleaseClient();
            }
        }
        if (_client is null)
        {
            _client = ActivateDefaultSpeakers();
            InitClient(_client, Pcm16(), allowFloat: true);
            Source = "whole PC";
        }
        var iid = typeof(IAudioCaptureClient).GUID;
        Check(_client.GetService(ref iid, out var service));
        _capture = (IAudioCaptureClient)service;
    }

    /// <summary>Records until <paramref name="ct"/> is cancelled; <paramref name="onPcm"/> gets the capture time (µs) and PCM bytes.</summary>
    public void Run(Action<long, byte[]> onPcm, CancellationToken ct)
    {
        Check(_client!.Start());
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(5);
                while (_capture!.GetNextPacketSize(out uint size) == 0 && size > 0)
                {
                    Check(_capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _));
                    long now = Clock.Micros;
                    var pcm = new byte[frames * BytesPerFrame];
                    if ((flags & 0x2) == 0) // not marked silent
                    {
                        if (_float) FloatToPcm16(data, (int)frames * Channels, pcm);
                        else Marshal.Copy(data, pcm, 0, pcm.Length);
                    }
                    _capture.ReleaseBuffer(frames);
                    long start = now - frames * 1_000_000L / SampleRate;
                    if (pcm.Length > 0) onPcm(start, pcm);
                }
            }
        }
        finally { _client.Stop(); }
    }

    internal static unsafe void FloatToPcm16(IntPtr source, int samples, byte[] into)
    {
        var f = (float*)source;
        fixed (byte* b = into)
        {
            var s = (short*)b;
            for (int i = 0; i < samples; i++) s[i] = (short)Math.Clamp((int)(f[i] * 32767f), short.MinValue, short.MaxValue);
        }
    }

    private void InitClient(IAudioClient client, WaveFormat format, bool allowFloat)
    {
        const int sharedMode = 0, loopback = 0x20000, autoConvert = unchecked((int)0x80000000), defaultQuality = 0x08000000;
        const long bufferHns = 200_000;  // 20 ms
        int hr = client.Initialize(sharedMode, loopback | autoConvert | defaultQuality, bufferHns, 0, ref format, IntPtr.Zero);
        if (hr == 0) return;
        if (!allowFloat) Check(hr);
        // Older Windows will not convert a loopback stream for us: take the system mix as float and convert it here.
        var floatFormat = new WaveFormat
        {
            FormatTag = 3, Channels = Channels, SamplesPerSec = SampleRate, AvgBytesPerSec = SampleRate * 8, BlockAlign = 8, BitsPerSample = 32,
        };
        Check(client.Initialize(sharedMode, loopback, bufferHns, 0, ref floatFormat, IntPtr.Zero));
        _float = true;
    }

    private static WaveFormat Pcm16() => new()
    {
        FormatTag = 1, Channels = Channels, SamplesPerSec = SampleRate, AvgBytesPerSec = SampleRate * BytesPerFrame, BlockAlign = BytesPerFrame, BitsPerSample = 16,
    };

    private static IAudioClient ActivateDefaultSpeakers()
    {
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(0 /* speakers */, 0 /* console */, out var device));
            var iid = typeof(IAudioClient).GUID;
            Check(device.Activate(ref iid, 1 /* in-process */, IntPtr.Zero, out var client));
            Marshal.ReleaseComObject(device);
            return (IAudioClient)client;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static IAudioClient ActivateForProcess(int processId)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { PROCESS_LOOPBACK, { pid, INCLUDE_TARGET_PROCESS_TREE } } in a VT_BLOB PROPVARIANT.
        var parameters = Marshal.AllocHGlobal(12);
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parameters, 0, 1);
            Marshal.WriteInt32(parameters, 4, processId);
            Marshal.WriteInt32(parameters, 8, 0);
            for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(variant, i, 0);
            Marshal.WriteInt16(variant, 0, 65);              // VT_BLOB
            Marshal.WriteInt32(variant, 8, 12);              // cbSize
            Marshal.WriteIntPtr(variant, 16, parameters);    // pBlobData
            var handler = new ActivationHandler();
            var iid = typeof(IAudioClient).GUID;
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, variant, handler, out var operation));
            try
            {
                if (!handler.Done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("per-app sound capture did not start");
                Check(operation.GetActivateResult(out int result, out var client));
                Check(result);
                return (IAudioClient)client;
            }
            finally { Marshal.ReleaseComObject(operation); }
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private void ReleaseClient()
    {
        if (_capture is not null) { Marshal.ReleaseComObject(_capture); _capture = null; }
        if (_client is not null) { Marshal.ReleaseComObject(_client); _client = null; }
    }

    public void Dispose() => ReleaseClient();

    [ComVisible(true)]
    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new();
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation) { Done.Set(); return 0; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public short FormatTag, Channels;
        public int SamplesPerSec, AvgBytesPerSec;
        public short BlockAlign, BitsPerSample, ExtraSize;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid,
        IntPtr activationParams, IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, ref WaveFormat format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
