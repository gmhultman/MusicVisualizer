using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MusicVisualizer.Audio;

/// <summary>
/// Captures whatever the default output device is playing (WASAPI loopback), downmixes it to mono
/// and keeps the most recent samples in a ring buffer. Follows the default device when it changes
/// (e.g. headphones plugged in) and retries if no device is available.
/// </summary>
public sealed class LoopbackCapture : IDisposable, IMMNotificationClient
{
    private const int RingSize = 16384;
    private const long StaleAfterMs = 200;

    private readonly object _bufferLock = new();
    private readonly object _captureLock = new();
    private readonly float[] _ring = new float[RingSize];
    private int _writePos;
    private long _lastDataTicks;

    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _format;
    private int _restartPending;
    private volatile bool _disposed;

    public int SampleRate { get; private set; } = 48000;

    /// <summary>Empty while capturing normally; otherwise a short user-facing problem description.</summary>
    public string Status { get; private set; } = "Starting audio capture…";

    public string DeviceName { get; private set; } = "";

    public void Start()
    {
        try { _enumerator.RegisterEndpointNotificationCallback(this); }
        catch { /* device-change following is best effort */ }
        StartCapture();
    }

    /// <summary>Copies the most recent <c>dest.Length</c> mono samples into <paramref name="dest"/>.
    /// Returns zeros when nothing has been playing recently.</summary>
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
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _capture = new WasapiLoopbackCapture(_device);
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
                Status = ex is System.Runtime.InteropServices.COMException
                    ? "No audio output device found — retrying…"
                    : $"Audio capture failed: {ex.Message}";
                ScheduleRestart(2000);
            }
        }
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
                _ring[_writePos] = sum / channels;
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
        if (flow == DataFlow.Render && role == Role.Multimedia)
            ScheduleRestart(300);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
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
