using MusicVisualizer.Audio;

namespace MusicVisualizer.Tests;

public class SpectrumAnalyzerTests
{
    private const int SampleRate = 48000;
    private const float Dt = 1f / 60f;

    private static float[] Sine(float hz, float amplitude, int count, int offset = 0)
    {
        var s = new float[count];
        for (int i = 0; i < count; i++)
            s[i] = amplitude * MathF.Sin(2 * MathF.PI * hz * (i + offset) / SampleRate);
        return s;
    }

    private static int ArgMax(float[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] > values[best]) best = i;
        return best;
    }

    [Fact]
    public void Low_sample_rate_devices_still_light_the_top_band()
    {
        // A 16 kHz headset mic can't capture above 8 kHz; the bands squeeze down so the top one still gets signal.
        const int rate = 16000;
        var analyzer = new SpectrumAnalyzer();
        var s = new float[analyzer.FftSize];
        for (int f = 0; f < 20; f++)
        {
            for (int i = 0; i < s.Length; i++)
                s[i] = 0.8f * MathF.Sin(2 * MathF.PI * 7000f * (i + f * 800) / rate);
            analyzer.Process(s, rate, Dt);
        }

        Assert.Equal(analyzer.BandCount - 1, analyzer.BandIndexForFrequency(7000f));
        Assert.True(analyzer.Frame.Bands[^1] > 0.5f);
    }

    [Theory]
    [InlineData(100f)]
    [InlineData(1000f)]
    [InlineData(5000f)]
    public void Sine_peaks_in_the_band_containing_its_frequency(float hz)
    {
        var analyzer = new SpectrumAnalyzer();
        for (int f = 0; f < 20; f++)
            analyzer.Process(Sine(hz, 0.8f, analyzer.FftSize, f * 800), SampleRate, Dt);

        // At the low end one FFT bin (~23 Hz) is wider than a band, so allow ±1 band.
        int expected = analyzer.BandIndexForFrequency(hz);
        int actual = ArgMax(analyzer.Frame.Bands);
        Assert.InRange(actual, expected - 1, expected + 1);
        Assert.True(analyzer.Frame.Bands[actual] > 0.9f);
    }

    [Fact]
    public void Silence_produces_empty_frame()
    {
        var analyzer = new SpectrumAnalyzer();
        var silence = new float[analyzer.FftSize];
        AudioFrame frame = analyzer.Frame;
        for (int f = 0; f < 30; f++)
        {
            frame = analyzer.Process(silence, SampleRate, Dt);
            Assert.False(frame.Beat);
        }

        Assert.All(frame.Bands, b => Assert.Equal(0f, b));
        Assert.Equal(0f, frame.Level);
        Assert.Equal(0f, frame.Bass);
    }

    [Fact]
    public void Bass_onset_after_silence_triggers_a_beat()
    {
        var analyzer = new SpectrumAnalyzer();
        var silence = new float[analyzer.FftSize];
        for (int f = 0; f < 60; f++)
            analyzer.Process(silence, SampleRate, Dt);

        var frame = analyzer.Process(Sine(60f, 0.7f, analyzer.FftSize), SampleRate, Dt);

        Assert.True(frame.Beat);
        Assert.True(frame.Pulse > 0);
    }

    [Fact]
    public void Steady_tone_stops_triggering_beats()
    {
        var analyzer = new SpectrumAnalyzer();
        int beatsInLastSecond = 0;
        for (int f = 0; f < 240; f++) // 4 seconds
        {
            var frame = analyzer.Process(Sine(60f, 0.7f, analyzer.FftSize, f * 800), SampleRate, Dt);
            if (f >= 180 && frame.Beat) beatsInLastSecond++;
        }
        Assert.Equal(0, beatsInLastSecond);
    }

    [Fact]
    public void Treble_tone_does_not_trigger_beats()
    {
        var analyzer = new SpectrumAnalyzer();
        var silence = new float[analyzer.FftSize];
        for (int f = 0; f < 60; f++)
            analyzer.Process(silence, SampleRate, Dt);

        var frame = analyzer.Process(Sine(3000f, 0.7f, analyzer.FftSize), SampleRate, Dt);
        Assert.False(frame.Beat);
    }
}
