namespace MusicVisualizer.Audio;

/// <summary>Glues the audio capture to the analyzer; call <see cref="Update"/> once per rendered frame.</summary>
public sealed class AudioEngine : IDisposable
{
    private readonly AudioCapture _capture = new();
    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly float[] _samples;

    public AudioEngine()
    {
        _samples = new float[_analyzer.FftSize];
    }

    public AudioFrame Frame => _analyzer.Frame;
    public int BandCount => _analyzer.BandCount;
    public string Status => _capture.Status;
    public string DeviceName => _capture.DeviceName;
    public AudioSource Source => _capture.Source;

    public void Start() => _capture.Start();

    /// <inheritdoc cref="AudioCapture.Configure"/>
    public void Configure(AudioSource source, string? microphoneId, float microphoneGain) =>
        _capture.Configure(source, microphoneId, microphoneGain);

    public IReadOnlyList<AudioDeviceInfo> ListMicrophones() => _capture.ListMicrophones();

    public AudioFrame Update(float dt)
    {
        _capture.ReadLatest(_samples);
        return _analyzer.Process(_samples, _capture.SampleRate, dt);
    }

    public void Dispose() => _capture.Dispose();
}
