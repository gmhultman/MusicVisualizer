using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace MusicVisualizer.Services;

/// <summary>
/// Checks GitHub Releases for a newer version, downloads it in the background, and applies it
/// on restart or exit. Inert unless the app was installed with Setup.exe: dev runs and the
/// copy that publish.ps1 makes report <see cref="IsInstalled"/> = false.
/// </summary>
public sealed class Updater
{
    private const string RepoUrl = "https://github.com/gmhultman/MusicVisualizer";

    /// <summary>Set to a local folder of packed releases to test updates without GitHub.</summary>
    private const string SourceOverrideVariable = "MUSICVIS_UPDATE_SOURCE";

    private readonly UpdateManager _manager;
    private bool _checking;

    public Updater()
    {
        var overridePath = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        _manager = string.IsNullOrWhiteSpace(overridePath)
            ? new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false))
            : new UpdateManager(overridePath);
    }

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The version the user is running, e.g. "1.2.0", or "dev" for an uninstalled build.</summary>
    public string VersionText => _manager.CurrentVersion?.ToString() ?? DevVersion();

    /// <summary>Version of an update that has been downloaded and is waiting for a restart, if any.</summary>
    public string? PendingVersion => _manager.UpdatePendingRestart?.Version.ToString();

    /// <summary>Raised on the calling thread once an update has finished downloading.</summary>
    public event Action<string>? UpdateReady;

    /// <summary>
    /// Looks for an update and downloads it. Returns a message for the user when
    /// <paramref name="manual"/> is true; automatic checks stay silent, since being offline is normal.
    /// </summary>
    public async Task<string?> CheckAsync(bool manual)
    {
        if (!IsInstalled)
            return manual ? "Updates are only available in the installed version." : null;
        if (PendingVersion is { } pending)
            return manual ? $"Version {pending} is ready. Restart to update." : null;
        if (_checking)
            return manual ? "Already checking for updates." : null;

        _checking = true;
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update == null)
                return manual ? $"You're up to date ({VersionText})." : null;

            await _manager.DownloadUpdatesAsync(update);
            var version = update.TargetFullRelease.Version.ToString();
            UpdateReady?.Invoke(version);
            return null; // UpdateReady already tells the user
        }
        catch (Exception ex)
        {
            return manual ? $"Couldn't check for updates: {ex.Message}" : null;
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Closes the app, installs the downloaded update and starts the new version.</summary>
    public void RestartToUpdate()
    {
        if (_manager.UpdatePendingRestart is { } pending)
            _manager.ApplyUpdatesAndRestart(pending);
    }

    /// <summary>On a normal exit, quietly installs a downloaded update once this process has ended.</summary>
    public void ApplyPendingOnExit()
    {
        if (_manager.UpdatePendingRestart is { } pending)
            _manager.WaitExitThenApplyUpdates(pending, silent: true, restart: false);
    }

    private static string DevVersion()
    {
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return info?.Split('+')[0] ?? "dev"; // drop the "+commit" suffix the SDK appends
    }
}
