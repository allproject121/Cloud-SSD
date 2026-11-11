using System.Text.Json;
using System.Threading.Channels;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.GameStream;

/// <summary>
/// One phone playing: sends picture and sound over the connection and plays the phone's touches on the PC. The
/// picture and touches only flow while the game is in front, so the phone can never see or click the rest of the PC.
/// When the Wi-Fi cannot keep up, pictures are skipped before they are encoded and the bitrate goes down; it goes
/// back up once the connection has kept up for a while.
/// </summary>
public sealed class GameStreamSession
{
    /// <summary>Pictures waiting to be sent before new ones are held back.</summary>
    private const int MaxQueuedPictures = 2;
    /// <summary>About 200 ms of sound; older sound is dropped rather than letting it lag the picture.</summary>
    private const int MaxQueuedAudioBytes = AudioLoopback.SampleRate * AudioLoopback.BytesPerFrame / 5;

    private readonly Stream _stream;
    private readonly GameStreamSettings _settings;
    private readonly Func<bool> _gameInFront;
    private readonly Func<int?> _gameProcessId;
    private readonly Func<(int Left, int Top, int Right, int Bottom)?> _gameArea;
    private readonly ILogger _log;
    private readonly Channel<(byte[] Bytes, bool Picture)> _outgoing = Channel.CreateUnbounded<(byte[], bool)>(new() { SingleReader = true });
    private int _queuedPictures, _queuedAudioBytes;
    private volatile bool _inFront;
    private int _heldBack;           // pictures not taken because the connection was behind, since the last bitrate check
    private ScreenEncoder? _encoder;
    private TouchInjector? _touch;
    private string? _soundSource;

    public GameStreamSession(Stream stream, GameStreamSettings settings, Func<bool> gameInFront, Func<int?> gameProcessId,
        Func<(int Left, int Top, int Right, int Bottom)?> gameArea, ILogger log)
    {
        _stream = stream;
        _settings = settings;
        _gameInFront = gameInFront;
        _gameProcessId = gameProcessId;
        _gameArea = gameArea;
        _log = log;
    }

    /// <summary>Plays until the phone leaves or <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writer = Task.Run(() => WriteLoopAsync(stop.Token), CancellationToken.None);
        AudioLoopback? audio = null;
        try
        {
            try { _encoder = new ScreenEncoder(_settings.Width, _settings.Height, _settings.Fps, _settings.BitrateKbps, _log); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Game stream could not start the screen encoder");
                SendInfo("The PC could not start its video encoder: " + ex.Message);
                await Task.Delay(500, CancellationToken.None);
                return;
            }
            _encoder.GameArea = _gameArea;
            var encoderForTouch = _encoder;
            _touch = new TouchInjector(() => encoderForTouch.CurrentArea);
            if (_settings.Sound)
            {
                try { audio = new AudioLoopback(_gameProcessId(), _log); }
                catch (Exception ex) { _log.LogWarning(ex, "Game stream runs without sound"); }
            }
            _inFront = _gameInFront();
            _soundSource = audio?.Source;
            SendInfo();

            var encoder = _encoder;
            var video = Task.Factory.StartNew(() =>
            {
                try { encoder.Run(MayEncode, OnPicture, stop.Token); }
                catch (Exception ex) when (!stop.IsCancellationRequested)
                {
                    _log.LogWarning(ex, "Game stream video stopped");
                    SendInfo("The PC's video encoder stopped: " + ex.Message);
                    stop.CancelAfter(500);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var sound = audio is null ? Task.CompletedTask : Task.Factory.StartNew(() => audio.Run(OnSound, stop.Token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var watch = WatchAsync(stop.Token);
            try { await ReadLoopAsync(stop.Token); }
            finally
            {
                stop.Cancel();
                _touch.LiftAll();
                await Task.WhenAll(Quiet(video), Quiet(sound), Quiet(watch));
            }
        }
        finally
        {
            stop.Cancel();
            _outgoing.Writer.TryComplete();
            await Quiet(writer);
            audio?.Dispose();
            _encoder?.Dispose();
        }
    }

    private long _lastHeldBack;
    private int _gameFps, _sentFps;

    private bool MayEncode()
    {
        if (!_inFront) return false;
        if (Volatile.Read(ref _queuedPictures) < MaxQueuedPictures) return true;
        // Asked every few milliseconds while behind: count it once per picture time.
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastHeldBack) >= 15)
        {
            Interlocked.Exchange(ref _lastHeldBack, now);
            Interlocked.Increment(ref _heldBack);
        }
        return false;
    }

    private void OnPicture(long captureMicros, bool keyframe, byte[] accessUnit)
    {
        Interlocked.Increment(ref _queuedPictures);
        if (!_outgoing.Writer.TryWrite((StreamProtocol.VideoFrame(captureMicros, keyframe, accessUnit), true)))
            Interlocked.Decrement(ref _queuedPictures);
    }

    private void OnSound(long captureMicros, byte[] pcm)
    {
        if (Volatile.Read(ref _queuedAudioBytes) > MaxQueuedAudioBytes) return;
        Interlocked.Add(ref _queuedAudioBytes, pcm.Length);
        _outgoing.Writer.TryWrite((StreamProtocol.AudioFrame(captureMicros, pcm), false));
    }

    private void SendInfo(string? error = null)
    {
        var info = new
        {
            width = _settings.Width,
            height = _settings.Height,
            fps = _settings.Fps,
            bitrateKbps = _encoder?.BitrateKbps ?? 0,
            inFront = _inFront,
            gameFps = _gameFps,
            sentFps = _sentFps,
            encoder = _encoder?.EncoderName,
            sound = _soundSource,
            error,
        };
        _outgoing.Writer.TryWrite((StreamProtocol.Frame(StreamProtocol.Info, default, JsonSerializer.SerializeToUtf8Bytes(info)), false));
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (bytes, picture) in _outgoing.Reader.ReadAllAsync(ct))
            {
                await _stream.WriteAsync(bytes, ct);
                if (picture) Interlocked.Decrement(ref _queuedPictures);
                else if (bytes[0] == StreamProtocol.Audio) Interlocked.Add(ref _queuedAudioBytes, -(bytes.Length - StreamProtocol.HeaderSize - 8));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var message = await StreamProtocol.ReadAsync(_stream, StreamProtocol.MaxFromPhone, ct);
            if (message is not var (type, payload)) return;
            switch (type)
            {
                case StreamProtocol.Touch:
                    if (_inFront && StreamProtocol.ParseTouch(payload) is { } fingers) _touch!.Apply(fingers);
                    break;
                case StreamProtocol.Ping when payload.Length == 8:
                    _outgoing.Writer.TryWrite((StreamProtocol.PongFrame(BitConverter.ToInt64(payload), Clock.Micros), false));
                    break;
                case StreamProtocol.NeedKeyframe:
                    _encoder!.RequestKeyframe();
                    break;
                case StreamProtocol.Bye:
                    return;
            }
        }
    }

    /// <summary>Follows the game in and out of the front, keeps held fingers alive, and adjusts the bitrate once a second.</summary>
    private async Task WatchAsync(CancellationToken ct)
    {
        int ticks = 0, calmSeconds = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(25, ct);
            _touch!.KeepAlive();
            if (++ticks % 8 == 0)
            {
                bool front = _gameInFront();
                if (front != _inFront)
                {
                    _inFront = front;
                    if (!front) _touch.LiftAll();
                    else _encoder!.RequestKeyframe();
                    SendInfo();
                }
            }
            if (ticks % 80 == 0)
            {
                // Every 2 s: how fast the PC draws the game and how much of it reaches the phone.
                var (sent, drawn) = _encoder!.TakeCounts();
                _sentFps = sent / 2;
                _gameFps = drawn / 2;
                SendInfo();
                if (ticks % 400 == 0)
                    _log.LogInformation("Game stream: PC draws {Game} fps, sent {Sent} fps at {Kbps} kbps", _gameFps, _sentFps, _encoder.BitrateKbps);
            }
            if (ticks % 40 == 0)
            {
                int heldBack = Interlocked.Exchange(ref _heldBack, 0);
                int now = _encoder!.BitrateKbps, next = NextBitrate(now, heldBack, ref calmSeconds, _settings.MinBitrateKbps, _settings.BitrateKbps);
                if (next != now)
                {
                    _encoder.SetBitrate(next);
                    _log.LogDebug("Game stream bitrate {Old} → {New} kbps ({Held} pictures held back)", now, next, heldBack);
                }
            }
        }
    }

    /// <summary>Down by a quarter in a second the connection fell behind; up by a tenth after five calm seconds.</summary>
    internal static int NextBitrate(int now, int heldBack, ref int calmSeconds, int min, int max)
    {
        if (heldBack > 5)
        {
            calmSeconds = 0;
            return Math.Max(min, now * 3 / 4);
        }
        if (++calmSeconds < 5 || now >= max) return now;
        calmSeconds = 0;
        return Math.Min(max, now + Math.Max(250, now / 10));
    }

    private static async Task Quiet(Task task)
    {
        try { await task; } catch { }
    }
}
