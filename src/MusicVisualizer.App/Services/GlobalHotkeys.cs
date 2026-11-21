using System.Windows.Interop;
using MusicVisualizer.Interop;

namespace MusicVisualizer.Services;

/// <summary>
/// System-wide hotkeys on a hidden message-only window, so they keep working while the
/// visualizer is a click-through overlay (or windows are being recreated).
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        var p = new HwndSourceParameters("MusicVisualizerHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers Ctrl+Alt+<paramref name="key"/>. Returns false if another app already owns it.</summary>
    public bool RegisterCtrlAlt(System.Windows.Input.Key key, Action action)
    {
        int id = _nextId++;
        uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, vk))
            return false;
        _actions[id] = action;
        return true;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _source.Dispose();
    }
}
