using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace CloudSSD.GameStream;

/// <summary>
/// Turns the PC screen into H.264 for the phone, all on the graphics card: Windows' desktop duplication hands over
/// each new screen image, the GPU's video processor scales it and converts it to NV12, and the GPU's own H.264
/// encoder (an asynchronous Media Foundation transform) compresses it. Tuned for delay, not size: constant bitrate,
/// no B-frames, low-latency mode and no keyframes unless the phone asks for one.
/// </summary>
public sealed class ScreenEncoder : IDisposable
{
    private const int Ring = 4;
    private static readonly Guid MajorVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid SubtypeNV12 = new("3231564E-0000-0010-8000-00AA00389B71");
    private static readonly Guid SubtypeH264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid EncoderCategory = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    private static readonly Guid MtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MtInterlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MtPixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MtProfile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid CleanPoint = new("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");
    private static readonly Guid ApiRateControl = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    private static readonly Guid ApiMeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");
    private static readonly Guid ApiLowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    private static readonly Guid ApiGopSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    private static readonly Guid ApiBFrames = new("8d390aac-dc5c-4200-b57f-814d04babab2");
    private static readonly Guid ApiQualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");
    private static readonly Guid ApiForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");

    // Vortice has no names for these Media Foundation numbers.
    private const int MessageSetD3DManager = 2, MessageBeginStreaming = 0x10000000, MessageStartOfStream = 0x10000003;
    private const int EventNeedInput = 601, EventHaveOutput = 602;
    private const int DxgiWaitTimeout = unchecked((int)0x887A0027), DxgiAccessLost = unchecked((int)0x887A0026);

    private readonly int _width, _height, _fps;
    private readonly ILogger _log;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly IDXGIOutput1 _output;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _screen;
    private ID3D11VideoProcessorEnumerator? _processorEnum;
    private ID3D11VideoProcessor? _processor;
    private ID3D11VideoProcessorInputView? _screenView;
    private readonly ID3D11Texture2D[] _nv12 = new ID3D11Texture2D[Ring];
    private readonly ID3D11VideoProcessorOutputView?[] _nv12Views = new ID3D11VideoProcessorOutputView?[Ring];
    private readonly IMFDXGIDeviceManager _manager;
    private readonly IMFTransform _encoder;
    private readonly ICodecApi _codec;
    private int _screenWidth, _screenHeight;
    private volatile int _wantedBitrateKbps;
    private int _bitrateKbps;
    private volatile bool _wantKeyframe;

    public string EncoderName { get; }
    /// <summary>Where the captured screen is on the desktop, in physical pixels (touches are mapped onto it).</summary>
    public (int Left, int Top, int Right, int Bottom) ScreenBounds { get; }
    /// <summary>The part of the desktop to send (the emulator's game area), or null for the whole screen. Asked before every picture.</summary>
    public Func<(int Left, int Top, int Right, int Bottom)?>? GameArea { get; set; }
    /// <summary>The desktop area the last picture showed, in physical pixels; touches are placed onto it.</summary>
    public (int Left, int Top, int Right, int Bottom) CurrentArea => _currentArea;
    private (int Left, int Top, int Right, int Bottom) _currentArea;
    private RawRect _source;
    public int BitrateKbps => _bitrateKbps;

    public ScreenEncoder(int width, int height, int fps, int bitrateKbps, ILogger log)
    {
        _width = width & ~1;
        _height = height & ~1;
        _fps = Math.Clamp(fps, 10, 120);
        _bitrateKbps = _wantedBitrateKbps = bitrateKbps;
        _log = log;

        MediaFactory.MFStartup().CheckError();
        D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware,
            DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out _device, out _context).CheckError();
        // The encoder uses the device from its own threads.
        using (var multithread = _device.QueryInterface<ID3D11Multithread>()) multithread.SetMultithreadProtected(true);
        _videoDevice = _device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = _context.QueryInterface<ID3D11VideoContext>();

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();
        using (adapter)
        {
            adapter.EnumOutputs(0, out IDXGIOutput output).CheckError();
            using (output) _output = output.QueryInterface<IDXGIOutput1>();
        }
        var desk = _output.Description.DesktopCoordinates;
        ScreenBounds = _currentArea = (desk.Left, desk.Top, desk.Right, desk.Bottom);
        StartDuplication();

        for (int i = 0; i < Ring; i++)
        {
            _nv12[i] = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1, Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.VideoEncoder,
            });
        }
        BuildProcessor();

        (_encoder, EncoderName) = CreateEncoder();
        _encoder.Attributes.Set(TransformAttributeKeys.TransformAsyncUnlock, 1u);
        _manager = MediaFactory.MFCreateDXGIDeviceManager();
        _manager.ResetDevice(_device).CheckError();
        _encoder.ProcessMessage((TMessageType)MessageSetD3DManager, (UIntPtr)(ulong)_manager.NativePointer);

        _codec = (ICodecApi)Marshal.GetObjectForIUnknown(_encoder.NativePointer);
        SetCodec(ApiRateControl, 0u);               // constant bitrate
        SetCodec(ApiMeanBitRate, (uint)_bitrateKbps * 1000);
        SetCodec(ApiLowLatency, true);
        SetCodec(ApiGopSize, 0xFFFFFFFFu);           // keyframes only when asked
        SetCodec(ApiBFrames, 0u);
        SetCodec(ApiQualityVsSpeed, 30u);

        var outType = MediaFactory.MFCreateMediaType();
        outType.Set(MtMajorType, MajorVideo);
        outType.Set(MtSubtype, SubtypeH264);
        outType.Set(MtFrameSize, MediaFactory.PackSize((uint)_width, (uint)_height));
        outType.Set(MtFrameRate, MediaFactory.PackRatio(_fps, 1));
        outType.Set(MtPixelAspect, MediaFactory.PackRatio(1, 1));
        outType.Set(MtAvgBitrate, (uint)_bitrateKbps * 1000);
        outType.Set(MtInterlace, 2u);                // progressive
        outType.Set(MtProfile, 66u);                 // baseline: every phone decodes it with little delay
        _encoder.SetOutputType(0, outType, 0);
        var inType = MediaFactory.MFCreateMediaType();
        inType.Set(MtMajorType, MajorVideo);
        inType.Set(MtSubtype, SubtypeNV12);
        inType.Set(MtFrameSize, MediaFactory.PackSize((uint)_width, (uint)_height));
        inType.Set(MtFrameRate, MediaFactory.PackRatio(_fps, 1));
        inType.Set(MtPixelAspect, MediaFactory.PackRatio(1, 1));
        inType.Set(MtInterlace, 2u);
        _encoder.SetInputType(0, inType, 0);
        _log.LogInformation("Game stream encoder: {Name}, screen {W}x{H} → {OW}x{OH} at {Fps} fps, {Kbps} kbps",
            EncoderName, _screenWidth, _screenHeight, _width, _height, _fps, _bitrateKbps);
    }

    /// <summary>Takes effect before the next picture.</summary>
    public void SetBitrate(int kbps) => _wantedBitrateKbps = kbps;

    public void RequestKeyframe() => _wantKeyframe = true;

    /// <summary>
    /// Encodes until <paramref name="ct"/> is cancelled. <paramref name="mayEncode"/> is asked before every picture;
    /// while it says no (game not in front, Wi-Fi backed up) no picture is taken, so nothing has to be thrown away
    /// later and the pictures that are sent still fit together. <paramref name="onFrame"/> gets the capture time in
    /// microseconds (<see cref="Clock"/>), whether it is a keyframe, and the Annex-B bytes.
    /// </summary>
    public void Run(Func<bool> mayEncode, Action<long, bool, byte[]> onFrame, CancellationToken ct)
    {
        _encoder.ProcessMessage((TMessageType)MessageBeginStreaming, UIntPtr.Zero);
        _encoder.ProcessMessage((TMessageType)MessageStartOfStream, UIntPtr.Zero);
        using var events = _encoder.QueryInterface<IMFMediaEventGenerator>();
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        long frameTicks = Stopwatch.Frequency / _fps, earliest = 0, lastSent = 0, idleTicks = Stopwatch.Frequency / 4;
        int slot = 0;
        while (true)
        {
            using var ev = events.GetEvent(0);
            int type = (int)ev.EventType;
            if (type == EventNeedInput)
            {
                // Send a picture the moment the game draws a new one (no faster than the fps cap). A still screen is
                // sent again four times a second so the phone knows the link is alive.
                bool take = false;
                long captured = 0;
                while (!ct.IsCancellationRequested)
                {
                    while (Stopwatch.GetTimestamp() < earliest && !ct.IsCancellationRequested) Thread.Sleep(1);
                    if (!mayEncode()) { Thread.Sleep(4); continue; }
                    bool fresh = GrabScreen(20);
                    long now = Stopwatch.GetTimestamp();
                    if (fresh || now - lastSent >= idleTicks)
                    {
                        take = true;
                        captured = Clock.Micros;
                        earliest = now + frameTicks * 9 / 10;   // a little slack, so 60 fps from the game stays 60
                        lastSent = now;
                        break;
                    }
                }
                // Stopping: what the encoder still holds is not needed, the connection is closing.
                if (!take) break;

                Interlocked.Increment(ref _encoded);
                ApplyChanges();
                ChooseSource();
                var view = _nv12Views[slot]!;
                var target = _nv12[slot];
                slot = (slot + 1) % Ring;
                _videoContext.VideoProcessorBlt(_processor!, view, 0, 1,
                    [new VideoProcessorStream { Enable = true, InputSurface = _screenView! }]).CheckError();
                using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, target, 0, false);
                using var sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                sample.SampleTime = captured * 10;   // 100 ns units; comes back on the output, so the capture time travels along
                sample.SampleDuration = 10_000_000L / _fps;
                _encoder.ProcessInput(0, sample, 0);
            }
            else if (type == EventHaveOutput)
            {
                var data = new OutputDataBuffer { StreamID = 0 };
                var result = _encoder.ProcessOutput(0, 1, ref data, out _);
                data.Events?.Dispose();
                if (result.Failure) throw new COMException("The encoder failed", result.Code);
                using var sample = data.Sample;
                bool key = sample.GetUInt32(CleanPoint, out uint clean).Success && clean != 0;
                using var buffer = sample.ConvertToContiguousBuffer();
                buffer.Lock(out var pointer, out _, out var length);
                var bytes = new byte[length];
                try { Marshal.Copy(pointer, bytes, 0, length); }
                finally { buffer.Unlock(); }
                if (!ct.IsCancellationRequested) onFrame(sample.SampleTime / 10, key, bytes);
            }
        }
    }

    private void ApplyChanges()
    {
        if (_wantedBitrateKbps != _bitrateKbps)
        {
            _bitrateKbps = _wantedBitrateKbps;
            SetCodec(ApiMeanBitRate, (uint)_bitrateKbps * 1000);
        }
        if (_wantKeyframe)
        {
            _wantKeyframe = false;
            SetCodec(ApiForceKeyFrame, 1u);
        }
    }

    /// <summary>Crops the picture to the game area when there is one, so the phone sees only the game.</summary>
    private void ChooseSource()
    {
        var (dl, dt, _, _) = ScreenBounds;
        var source = new RawRect(0, 0, _screenWidth, _screenHeight);
        if (GameArea?.Invoke() is { } a)
        {
            int l = Math.Max(0, a.Left - dl), t = Math.Max(0, a.Top - dt);
            int r = Math.Min(_screenWidth, a.Right - dl), b = Math.Min(_screenHeight, a.Bottom - dt);
            if (r - l >= 64 && b - t >= 64) source = new RawRect(l, t, r, b);
        }
        if (source.Equals(_source)) return;
        _source = source;
        _videoContext.VideoProcessorSetStreamSourceRect(_processor!, 0, true, source);
        _currentArea = (source.Left + dl, source.Top + dt, source.Right + dl, source.Bottom + dt);
    }

    /// <summary>
    /// Waits up to <paramref name="waitMs"/> for the screen to change and copies the newest image. False when nothing
    /// new was drawn (the previous image stays).
    /// </summary>
    private bool GrabScreen(int waitMs)
    {
        if (_duplication is null && !TryRestart()) { Thread.Sleep(waitMs); return false; }
        var result = _duplication!.AcquireNextFrame((uint)waitMs, out var info, out var resource);
        if (result.Code == DxgiWaitTimeout) return false;
        if (result.Code == DxgiAccessLost || result.Failure)
        {
            // A UAC prompt, a lock screen, a full-screen mode switch or a new resolution: start again.
            resource?.Dispose();
            _duplication.Dispose();
            _duplication = null;
            TryRestart();
            return false;
        }
        bool drawn = info.LastPresentTime != 0;   // 0: only the mouse pointer moved
        using (resource)
        using (var texture = resource.QueryInterface<ID3D11Texture2D>())
        {
            var d = texture.Description;
            if (drawn && d.Width == (uint)_screenWidth && d.Height == (uint)_screenHeight) _context.CopyResource(_screen!, texture);
        }
        _duplication.ReleaseFrame();
        if (drawn) Interlocked.Increment(ref _drawn);
        return drawn;
    }

    private int _encoded, _drawn;

    /// <summary>Pictures sent and screen images the PC drew since the last call (the game's own fps, roughly).</summary>
    public (int Sent, int Drawn) TakeCounts() => (Interlocked.Exchange(ref _encoded, 0), Interlocked.Exchange(ref _drawn, 0));

    private bool TryRestart()
    {
        try
        {
            int oldW = _screenWidth, oldH = _screenHeight;
            StartDuplication();
            if (_screenWidth != oldW || _screenHeight != oldH) BuildProcessor();
            return true;
        }
        catch (SharpGenException ex)
        {
            _log.LogDebug(ex, "Screen capture not available yet");
            _duplication = null;
            return false;
        }
    }

    private void StartDuplication()
    {
        _duplication = _output.DuplicateOutput(_device);
        var mode = _duplication.Description.ModeDescription;
        _screenWidth = (int)mode.Width;
        _screenHeight = (int)mode.Height;
    }

    private void BuildProcessor()
    {
        _screenView?.Dispose();
        _screen?.Dispose();
        _processor?.Dispose();
        foreach (var v in _nv12Views) v?.Dispose();
        _processorEnum?.Dispose();

        _processorEnum = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)_screenWidth, InputHeight = (uint)_screenHeight,
            OutputWidth = (uint)_width, OutputHeight = (uint)_height,
            InputFrameRate = new Rational((uint)_fps, 1), OutputFrameRate = new Rational((uint)_fps, 1),
            Usage = VideoUsage.OptimalSpeed,
        });
        _processor = _videoDevice.CreateVideoProcessor(_processorEnum, 0);
        _source = default;   // a new processor starts with the whole screen
        _screen = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_screenWidth, Height = (uint)_screenHeight, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });
        _screenView = _videoDevice.CreateVideoProcessorInputView(_screen, _processorEnum,
            new VideoProcessorInputViewDescription { ViewDimension = VideoProcessorInputViewDimension.Texture2D });
        for (int i = 0; i < Ring; i++)
        {
            _nv12Views[i] = _videoDevice.CreateVideoProcessorOutputView(_nv12[i], _processorEnum,
                new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });
        }
    }

    private static (IMFTransform, string) CreateEncoder()
    {
        const uint hardware = 0x4, sortAndFilter = 0x40;
        var output = new RegisterTypeInfo { GuidMajorType = MajorVideo, GuidSubtype = SubtypeH264 };
        using var found = MediaFactory.MFTEnumEx(EncoderCategory, hardware | sortAndFilter, null, output);
        foreach (var activate in found)
        {
            try
            {
                string name;
                try { name = activate.GetString(new Guid("314ffbae-5b41-4c95-9c19-4e7d586face3")); }
                catch (SharpGenException) { name = "hardware H.264"; }
                return (activate.ActivateObject<IMFTransform>(), name);
            }
            catch (SharpGenException) { }
        }
        throw new NotSupportedException("This PC has no hardware H.264 encoder that Windows can use.");
    }

    private void SetCodec(Guid api, object value)
    {
        int hr = _codec.SetValue(ref api, ref value);
        if (hr != 0) _log.LogDebug("Encoder setting {Api} refused: 0x{Hr:x8}", api, hr);
    }

    public void Dispose()
    {
        try { _encoder.ProcessMessage((TMessageType)0x10000001, UIntPtr.Zero); } catch (SharpGenException) { } // end of streaming
        Marshal.ReleaseComObject(_codec);
        _encoder.Dispose();
        _manager.Dispose();
        _screenView?.Dispose();
        foreach (var v in _nv12Views) v?.Dispose();
        foreach (var t in _nv12) t?.Dispose();
        _processor?.Dispose();
        _processorEnum?.Dispose();
        _screen?.Dispose();
        _duplication?.Dispose();
        _output.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
        _context.Dispose();
        _device.Dispose();
        MediaFactory.MFShutdown();
    }

    [ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICodecApi
    {
        [PreserveSig] int IsSupported(ref Guid api);
        [PreserveSig] int IsModifiable(ref Guid api);
        [PreserveSig] int GetParameterRange(ref Guid api, out object min, out object max, out object step);
        [PreserveSig] int GetParameterValues(ref Guid api, IntPtr values, out uint count);
        [PreserveSig] int GetDefaultValue(ref Guid api, out object value);
        [PreserveSig] int GetValue(ref Guid api, out object value);
        [PreserveSig] int SetValue(ref Guid api, ref object value);
    }
}

/// <summary>One clock for picture, sound and pings, in microseconds since the PC started.</summary>
public static class Clock
{
    public static long Micros => (long)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));
}
