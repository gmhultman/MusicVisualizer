using Velopack;

namespace MusicVisualizer;

/// <summary>
/// Custom entry point so Velopack can run first: during install, update and uninstall the
/// installer launches the exe with special arguments, and <see cref="VelopackApp.Run"/>
/// handles those and exits before any window or tray icon appears.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
