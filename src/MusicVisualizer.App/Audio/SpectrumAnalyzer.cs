using NAudio.Dsp;

namespace MusicVisualizer.Audio;

/// <summary>
/// Turns a block of mono samples into log-spaced frequency bands (0..1) with auto-gain,
/// attack/release smoothing, falling peak markers and a simple bass-onset beat detector.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const float DynamicRangeDb = 54f;     // range mapped onto 0..1
    public const float MinCeilingDb = -42f;      // auto-gain won't amplify beyond this (keeps noise dark)
    public const float CeilingReleaseDbPerSec = 6f;
    public const float TiltDbPerOctave = 3f;     // compensates music's natural high-frequency roll-off

    public const float BeatThreshold = 1.5f;     // bass energy vs. its rolling average
    public const float MinBeatEnergy = 1e-4f;
    public const float BeatCooldownSec = 0.22f;

    private readonly int _log2Size;
    private readonly Complex[] _fft;
    private readonly float[] _window;
    private readonly float[] _mag;
    private readonly float[] _bandLoHz;
    private readonly float[] _bandHiHz;
    private readonly float[] _bandCenterHz;
    private readonly float[] _peakVelocity;
    private readonly float[] _peakHold;

    private readonly float _minHz, _maxHz;
    private int _bandRate; // sample rate the band edges were laid out for

    private float _ceilingDb = MinCeilingDb;
    private float _bassAverage;
    private float _sinceBeat = 10f;

    public SpectrumAnalyzer(int fftSize = 2048, int bandCount = 64, float minHz = 30f, float maxHz = 16000f)
    {
        if ((fftSize & (fftSize - 1)) != 0) throw new ArgumentException("FFT size must be a power of two.");
        FftSize = fftSize;
        BandCount = bandCount;
        _log2Size = (int)Math.Log2(fftSize);
        _fft = new Complex[fftSize];
        _window = new float[fftSize];
        _mag = new float[fftSize / 2 + 1];
        for (int i = 0; i < fftSize; i++)
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1))));

        _bandLoHz = new float[bandCount];
        _bandHiHz = new float[bandCount];
        _bandCenterHz = new float[bandCount];
        _minHz = minHz;
        _maxHz = maxHz;
        LayOutBands(48000);

        RawBands = new float[bandCount];
        _peakVelocity = new float[bandCount];
        _peakHold = new float[bandCount];
        Frame = new AudioFrame(bandCount);
    }

    /// <summary>
    /// Spreads the log-spaced bands from minHz up to maxHz, or to just below the Nyquist frequency when the device
    /// records at a low rate (e.g. a 16 kHz headset mic), so every band carries signal.
    /// </summary>
    private void LayOutBands(int sampleRate)
    {
        _bandRate = sampleRate;
        float top = MathF.Min(_maxHz, sampleRate * 0.45f);
        double ratio = top / _minHz;
        for (int b = 0; b < BandCount; b++)
        {
            _bandLoHz[b] = (float)(_minHz * Math.Pow(ratio, (double)b / BandCount));
            _bandHiHz[b] = (float)(_minHz * Math.Pow(ratio, (double)(b + 1) / BandCount));
            _bandCenterHz[b] = MathF.Sqrt(_bandLoHz[b] * _bandHiHz[b]);
        }
    }

    public int FftSize { get; }
    public int BandCount { get; }

    /// <summary>Unsmoothed band levels from the most recent call to <see cref="Process"/>.</summary>
    public float[] RawBands { get; }

    public AudioFrame Frame { get; }

    public float BandCenterHz(int band) => _bandCenterHz[band];

    public int BandIndexForFrequency(float hz)
    {
        for (int b = 0; b < BandCount; b++)
            if (hz < _bandHiHz[b]) return b;
        return BandCount - 1;
    }

    /// <param name="samples">At least <see cref="FftSize"/> mono samples; the most recent are used.</param>
    /// <param name="dt">Seconds since the previous call (drives smoothing and decay).</param>
    public AudioFrame Process(ReadOnlySpan<float> samples, int sampleRate, float dt)
    {
        int n = FftSize;
        if (sampleRate > 0 && sampleRate != _bandRate) LayOutBands(sampleRate);
        if (samples.Length > n) samples = samples[^n..];
        int pad = n - samples.Length;

        // Window + FFT, and RMS level while we're walking the samples.
        double sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            float s = i < pad ? 0f : samples[i - pad];
            sumSq += s * s;
            _fft[i].X = s * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, _log2Size, _fft);

        // NAudio scales the forward FFT by 1/n; Hann coherent gain is 0.5 and the energy is split
        // between positive and negative frequencies — so x4 makes a full-scale sine read ~1.0.
        float binHz = (float)sampleRate / n;
        float bassEnergy = 0;
        for (int k = 0; k < _mag.Length; k++)
        {
            float re = _fft[k].X, im = _fft[k].Y;
            float m = MathF.Sqrt(re * re + im * im) * 4f;
            _mag[k] = m;
            float hz = k * binHz;
            if (hz >= 30f && hz <= 150f) bassEnergy += m * m;
        }

        // Bands in dB, with high-frequency tilt compensation.
        Span<float> bandDb = stackalloc float[BandCount];
        float maxDb = float.NegativeInfinity;
        for (int b = 0; b < BandCount; b++)
        {
            int kLo = (int)MathF.Ceiling(_bandLoHz[b] / binHz);
            int kHi = Math.Min((int)MathF.Floor(_bandHiHz[b] / binHz), _mag.Length - 1);
            float m;
            if (kHi >= kLo)
            {
                m = 0;
                for (int k = kLo; k <= kHi; k++) m = MathF.Max(m, _mag[k]);
            }
            else
            {
                // Band narrower than one bin (low end): interpolate at the band's centre.
                float pos = Math.Min(_bandCenterHz[b] / binHz, _mag.Length - 1.001f);
                int k0 = (int)pos;
                m = _mag[k0] + (_mag[k0 + 1] - _mag[k0]) * (pos - k0);
            }
            float db = 20f * MathF.Log10(MathF.Max(m, 1e-9f)) + TiltDbPerOctave * MathF.Log2(_bandCenterHz[b] / 1000f);
            bandDb[b] = db;
            maxDb = MathF.Max(maxDb, db);
        }

        // Auto-gain: the ceiling jumps up to the loudest band, then slowly falls back.
        _ceilingDb = maxDb > _ceilingDb ? maxDb : _ceilingDb - CeilingReleaseDbPerSec * dt;
        _ceilingDb = MathF.Max(_ceilingDb, MinCeilingDb);
        float floorDb = _ceilingDb - DynamicRangeDb;

        float attack = 1f - MathF.Exp(-dt / 0.025f);
        float release = 1f - MathF.Exp(-dt / 0.22f);
        var bands = Frame.Bands;
        var peaks = Frame.Peaks;
        float bass = 0, mid = 0, treble = 0;
        int bassN = 0, midN = 0, trebleN = 0;

        for (int b = 0; b < BandCount; b++)
        {
            float raw = Math.Clamp((bandDb[b] - floorDb) / DynamicRangeDb, 0f, 1f);
            RawBands[b] = raw;

            float cur = bands[b];
            cur += (raw - cur) * (raw > cur ? attack : release);
            if (cur < 1e-4f) cur = 0;
            bands[b] = cur;

            // Peak caps: hold briefly, then fall with gravity.
            if (cur >= peaks[b])
            {
                peaks[b] = cur;
                _peakVelocity[b] = 0;
                _peakHold[b] = 0.35f;
            }
            else if ((_peakHold[b] -= dt) <= 0)
            {
                _peakVelocity[b] += 2.2f * dt;
                peaks[b] = MathF.Max(cur, peaks[b] - _peakVelocity[b] * dt);
            }

            float hz = _bandCenterHz[b];
            if (hz < 250f) { bass += cur; bassN++; }
            else if (hz < 4000f) { mid += cur; midN++; }
            else { treble += cur; trebleN++; }
        }

        Frame.Bass = bassN > 0 ? bass / bassN : 0;
        Frame.Mid = midN > 0 ? mid / midN : 0;
        Frame.Treble = trebleN > 0 ? treble / trebleN : 0;
        Frame.Level = Math.Clamp((float)Math.Sqrt(sumSq / n) * 2.5f, 0f, 1f);

        // Beat detection: bass energy spiking above its rolling (~0.8 s) average.
        _sinceBeat += dt;
        bool beat = bassEnergy > MinBeatEnergy
                    && bassEnergy > BeatThreshold * _bassAverage
                    && _sinceBeat >= BeatCooldownSec;
        if (beat)
        {
            _sinceBeat = 0;
            float ratio = _bassAverage > 0 ? bassEnergy / _bassAverage : 4f;
            Frame.BeatStrength = Math.Clamp((ratio - 1f) / 3f, 0.15f, 1f);
            Frame.Pulse = MathF.Max(Frame.Pulse, Frame.BeatStrength);
        }
        else
        {
            Frame.Pulse *= MathF.Exp(-dt / 0.18f);
        }
        Frame.Beat = beat;
        _bassAverage += (bassEnergy - _bassAverage) * (1f - MathF.Exp(-dt / 0.8f));

        return Frame;
    }
}
