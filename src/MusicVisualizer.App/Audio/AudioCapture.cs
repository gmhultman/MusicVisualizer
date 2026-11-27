using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MusicVisualizer.Audio;

/// <summary>Where the sound comes from.</summary>
public enum AudioSource
{
    /// <summary>Whatever the PC is playing (WASAPI loopback of the default output device).</summary>
    Speakers,

    /// <summary>A microphone or other recording device.</summary>
    Microphone,
}

/// <summary>A recording device the user can pick.</summary>
public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// Captures either what the default output device is playing (WASAPI loopback) or a microphone, downmixes it
/// to mono and keeps the most recent samples in a ring buffer. Follows the default device when it changes
/// (e.g. headphones plugged in) and retries if no device is available.
/// </summary>
public sealed class AudioCapture : IDisposable, IMMNotificationClient
{
    private const int RingSize = 16384;
    private const long StaleAfterMs = 200;
    private const int AccessDenied = unchecked((int)0x80070005);

    private readonly object _bufferLock = new();
    private readonly object _captureLock = new();
    private readonly float[] _ring = new float[RingSize];
    private int _writePos;
    private long _lastDataTicks;

    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private WasapiCapture? _capture; // WasapiLoopbackCapture derives from WasapiCapture
    private WaveFormat? _format;
    private int _restartPending;
    private volatile bool _disposed;

    private AudioSource _source = AudioSource.Speakers;
    private string? _microphoneId; // null = the Windows default microphone
    private volatile float _gain = 1f;

    public int SampleRate { get; private set; } = 48000;

    /// <summary>Empty while capturing normally; otherwise a short user-facing problem description.</summary>
    public string Status { get; private set; } = "Starting audio capture…";

    public string DeviceName { get; private set; } = "";

    public AudioSource Source => _source;

    public void Start()
    {
        try { _enumerator.RegisterEndpointNotificationCallback(this); }
        catch { /* device-change following is best effort */ }
        StartCapture();
    }

    /// <summary>
    /// Switches source/device (restarting capture only if they changed) and sets the microphone gain.
    /// <paramref name="microphoneId"/> null means the Windows default microphone.
    /// </summary>
    public void Configure(AudioSource source, string? microphoneId, float microphoneGain)
    {
        _gain = Math.Clamp(microphoneGain, 0.1f, 50f);
        lock (_captureLock)
        {
            if (source == _source && microphoneId == _microphoneId) return;
            _source = source;
            _microphoneId = microphoneId;
            Status = source == AudioSource.Microphone ? "Starting microphone…" : "Starting audio capture…";
        }
        StartCapture();
    }

    /// <summary>The active recording devices, for the Settings picker.</summary>
    public IReadOnlyList<AudioDeviceInfo> ListMicrophones()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (d) result.Add(new AudioDeviceInfo(d.ID, d.FriendlyName));
            }
        }
        catch { /* no devices / audio service unavailable */ }
        return result;
    }

    /// <summary>Copies the most recent <c>dest.Length</c> mono samples into <paramref name="dest"/>.
    /// Returns zeros when nothing has been captured recently.</summary>
    public void ReadLatest(float[] dest)
    {
        lock (_bufferLock)
        {
            if (Environment.TickCount64 - _lastDataTicks > StaleAfterMs)
            {
                Array.Clear(dest);
                return;
            }

            int n = Math.Min(dest.Length, RingSize);
            int start = (_writePos - n + RingSize) % RingSize;
            int firstChunk = Math.Min(n, RingSize - start);
            Array.Copy(_ring, start, dest, 0, firstChunk);
            if (firstChunk < n)
                Array.Copy(_ring, 0, dest, firstChunk, n - firstChunk);
        }
    }

    private void StartCapture()
    {
        lock (_captureLock)
        {
            if (_disposed) return;
            StopCaptureCore();
            bool mic = _source == AudioSource.Microphone;
            try
            {
                _device = mic ? OpenMicrophone() : _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _capture = mic ? new WasapiCapture(_device) : new WasapiLoopbackCapture(_device);
                _format = _capture.WaveFormat;
                SampleRate = _format.SampleRate;
                DeviceName = _device.FriendlyName;
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                Status = "";
            }
            catch (Exception ex)
            {
                StopCaptureCore();
                if (mic && ex.HResult == AccessDenied)
                {
                    // Retrying won't help until the user changes the privacy setting; check now and then.
                    Status = "Microphone access is blocked — allow it in Windows Settings › Privacy & security › Microphone";
                    ScheduleRestart(5000);
                }
                else
                {
                    Status = ex is COMException
                        ? mic ? "No microphone found — retrying…" : "No audio output device found — retrying…"
                        : $"Audio capture failed: {ex.Message}";
                    ScheduleRestart(2000);
                }
            }
        }
    }

    /// <summary>The chosen microphone, or the default one if none is chosen or it's no longer available.</summary>
    private MMDevice OpenMicrophone()
    {
        if (_microphoneId != null)
        {
            try
            {
                var d = _enumerator.GetDevice(_microphoneId);
                if (d.State == DeviceState.Active) return d;
                d.Dispose();
            }
            catch (Exception) { /* unplugged or removed: use the default */ }
        }
        return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
    }

    private void StopCaptureCore()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }
        _device?.Dispose();
        _device = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var fmt = _format;
        if (fmt == null || e.BytesRecorded == 0) return;

        int channels = fmt.Channels;
        int bytesPerSample = fmt.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        if (frameBytes == 0) return;
        int frames = e.BytesRecorded / frameBytes;
        var buf = e.Buffer;
        // Microphones are much quieter than playback, so they get the user's sensitivity boost.
        float scale = (_source == AudioSource.Microphone ? _gain : 1f) / channels;

        lock (_bufferLock)
        {
            int offset = 0;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += bytesPerSample switch
                    {
                        4 => BitConverter.ToSingle(buf, offset), // shared-mode mix format is IEEE float
                        2 => BitConverter.ToInt16(buf, offset) / 32768f,
                        3 => ((buf[offset] << 8 | buf[offset + 1] << 16 | buf[offset + 2] << 24) >> 8) / 8388608f,
                        _ => 0f,
                    };
                    offset += bytesPerSample;
                }
                _ring[_writePos] = Math.Clamp(sum * scale, -1f, 1f);
                _writePos = (_writePos + 1) % RingSize;
            }
            _lastDataTicks = Environment.TickCount64;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // An exception here usually means the device went away or was invalidated.
        if (!_disposed && e.Exception != null)
        {
            Status = "Audio device changed — reconnecting…";
            ScheduleRestart(500);
        }
    }

    private void ScheduleRestart(int delayMs)
    {
        if (_disposed || Interlocked.Exchange(ref _restartPending, 1) == 1) return;
        Task.Delay(delayMs).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref _restartPending, 0);
            if (!_disposed) StartCapture();
        });
    }

    // ---- IMMNotificationClient (called on a COM thread; never block here) ----

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        bool relevant = _source == AudioSource.Speakers
            ? flow == DataFlow.Render && role == Role.Multimedia
            : flow == DataFlow.Capture && role == Role.Console && _microphoneId == null;
        if (relevant) ScheduleRestart(300);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        // The chosen microphone was plugged back in.
        if (_source == AudioSource.Microphone && deviceId == _microphoneId && newState == DeviceState.Active)
            ScheduleRestart(300);
    }

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        lock (_captureLock) StopCaptureCore();
        _enumerator.Dispose();
    }
}
