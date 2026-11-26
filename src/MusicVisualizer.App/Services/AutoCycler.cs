using System.Windows.Threading;
using MusicVisualizer.Settings;

namespace MusicVisualizer.Services;

/// <summary>
/// Changes the visualizer and/or colours by itself, on a timer or when a new song starts, as set in
/// <see cref="AppSettings.CycleVisualizers"/> and <see cref="AppSettings.CycleColors"/>.
/// </summary>
public sealed class AutoCycler : IDisposable
{
    private readonly App _app;
    private readonly DispatcherTimer _timer = new();

    public AutoCycler(App app)
    {
        _app = app;
        _timer.Tick += (_, _) => OnTimer();
        Restart();
    }

    private AppSettings S => _app.Settings;

    private bool TimerNeeded => S.CycleVisualizers == CycleMode.Timer || S.CycleColors == ColorCycleMode.Timer;

    /// <summary>Starts the countdown again: after a manual change, or when the cycle settings change.</summary>
    public void Restart()
    {
        _timer.Stop();
        if (!TimerNeeded) return;
        _timer.Interval = TimeSpan.FromMinutes(S.CycleMinutes);
        _timer.Start();
    }

    private void OnTimer()
    {
        if (S.CycleVisualizers == CycleMode.Timer) _app.NextVisualizer(auto: true);
        if (S.CycleColors == ColorCycleMode.Timer) _app.NextPalette(auto: true);
    }

    /// <summary>Called by the app when <see cref="SongChangeDetector"/> reports a new song.</summary>
    public void OnSongChanged()
    {
        if (S.CycleVisualizers == CycleMode.Song) _app.NextVisualizer(auto: true);
        if (S.CycleColors == ColorCycleMode.Song) _app.NextPalette(auto: true);
    }

    public void Dispose() => _timer.Stop();
}
