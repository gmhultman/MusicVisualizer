using System.Drawing;
using System.Windows.Forms;
using MusicVisualizer.Settings;
using MusicVisualizer.Visuals;

namespace MusicVisualizer.Services;

/// <summary>Notification-area icon: the only way to control the app while it's a click-through overlay.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(App app)
    {
        _app = app;
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream)
            _image = new Icon(stream, SystemInformation.SmallIconSize); // picks the best-matching size from the .ico
        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = "Music Visualizer",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu(_icon.ContextMenuStrip);
        BuildMenu(_icon.ContextMenuStrip); // non-empty, or the first right-click won't open it
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _app.SetMode(DisplayMode.Windowed);
        };
    }

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        var s = _app.Settings;

        menu.Items.Add(new ToolStripMenuItem(s.Mode == DisplayMode.Overlay ? "Show window" : "Desktop overlay", null,
            (_, _) => _app.ToggleMode()) { ShortcutKeyDisplayString = "Ctrl+Alt+V" });
        menu.Items.Add(new ToolStripSeparator());

        var visuals = new ToolStripMenuItem("Visualizer") { ShortcutKeyDisplayString = "Ctrl+Alt+N" };
        for (int i = 0; i < _app.Visualizers.Count; i++)
        {
            int idx = i;
            visuals.DropDownItems.Add(new ToolStripMenuItem(_app.Visualizers[i].Name, null, (_, _) => _app.SetVisualizer(idx))
                { Checked = idx == _app.VisualizerIndex });
        }
        menu.Items.Add(visuals);

        var palettes = new ToolStripMenuItem("Colors") { ShortcutKeyDisplayString = "Ctrl+Alt+C" };
        for (int i = 0; i < Palette.All.Count; i++)
        {
            int idx = i;
            palettes.DropDownItems.Add(new ToolStripMenuItem(Palette.All[i].Name, null, (_, _) => _app.SetPalette(idx))
                { Checked = idx == s.PaletteIndex });
        }
        menu.Items.Add(palettes);

        var layout = new ToolStripMenuItem("Overlay layout");
        layout.DropDownItems.Add(new ToolStripMenuItem("Bottom strip", null, (_, _) => _app.SetOverlayLayout(OverlayLayout.BottomStrip))
            { Checked = s.OverlayLayout == OverlayLayout.BottomStrip });
        layout.DropDownItems.Add(new ToolStripMenuItem("Full screen", null, (_, _) => _app.SetOverlayLayout(OverlayLayout.FullScreen))
            { Checked = s.OverlayLayout == OverlayLayout.FullScreen });
        menu.Items.Add(layout);

        menu.Items.Add(new ToolStripMenuItem("Glow", null, (_, _) => _app.ToggleGlow()) { Checked = s.Glow });
        menu.Items.Add(new ToolStripSeparator());

        var updater = _app.Updater;
        menu.Items.Add(new ToolStripMenuItem($"Music Visualizer {updater.VersionText}") { Enabled = false });
        if (updater.PendingVersion is { } pending)
        {
            var restart = new ToolStripMenuItem($"Restart to update to {pending}", null, (_, _) => _app.RestartToUpdate());
            restart.Font = new Font(restart.Font, FontStyle.Bold);
            menu.Items.Add(restart);
        }
        else
        {
            menu.Items.Add(new ToolStripMenuItem("Check for updates", null, (_, _) => _app.CheckForUpdatesNow())
                { Enabled = updater.IsInstalled });
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => _app.Quit()));
    }

    /// <summary>Shows a notification from the tray icon.</summary>
    public void ShowMessage(string text) =>
        _icon.ShowBalloonTip(5000, "Music Visualizer", text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
