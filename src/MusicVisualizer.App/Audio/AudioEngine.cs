namespace MusicVisualizer.Audio;

/// <summary>Glues the loopback capture to the analyzer; call <see cref="Update"/> once per rendered frame.</summary>
public sealed class AudioEngine : IDisposable
{
    private readonly LoopbackCapture _capture = new();
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

    public void Start() => _capture.Start();

    public AudioFrame Update(float dt)
    {
        _capture.ReadLatest(_samples);
        return _analyzer.Process(_samples, _capture.SampleRate, dt);
    }

    public void Dispose() => _capture.Dispose();
}
