namespace MusicVisualizer.Audio;

/// <summary>Analysed audio for one rendered frame. Reused between frames — don't hold onto it.</summary>
public sealed class AudioFrame
{
    public AudioFrame(int bandCount)
    {
        Bands = new float[bandCount];
        Peaks = new float[bandCount];
    }

    /// <summary>Smoothed band levels, 0..1, low to high frequency (log spaced).</summary>
    public float[] Bands { get; }

    /// <summary>Falling peak-hold markers per band, 0..1.</summary>
    public float[] Peaks { get; }

    public float Bass { get; internal set; }
    public float Mid { get; internal set; }
    public float Treble { get; internal set; }

    /// <summary>Overall loudness, 0..1.</summary>
    public float Level { get; internal set; }

    /// <summary>True only on the frame a beat (bass onset) is detected.</summary>
    public bool Beat { get; internal set; }

    /// <summary>How strong the latest beat was, 0..1.</summary>
    public float BeatStrength { get; internal set; }

    /// <summary>Jumps to 1 on each beat and decays back to 0 — handy for pulsing effects.</summary>
    public float Pulse { get; internal set; }
}
