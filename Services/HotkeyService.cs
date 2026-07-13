using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TaskNinja.Services;

/// <summary>
/// Registers global hotkeys via Win32 RegisterHotKey. Mirrors the
/// ClipNinja HotkeyService — same WM_HOTKEY message hook, same
/// modifier flags. The single hotkey TaskNinja v1.0 cares about is
/// Ctrl+Shift+T to show/hide the main window.
/// </summary>
public class HotkeyService : IDisposable
{
    public const uint NoMod  = 0x0000;
    public const uint Alt    = 0x0001;
    public const uint Ctrl   = 0x0002;
    public const uint Shift  = 0x0004;
    public const uint Win    = 0x0008;
    public const uint CtrlShift = Ctrl | Shift;

    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private int _nextId = 9000;
    private readonly Dictionary<int, Action> _handlers = new();

    public HotkeyService(Window window)
    {
        _window = window;
        // RegisterHotKey needs the HWND, which exists only after SourceInitialized.
        if (PresentationSource.FromVisual(window) is HwndSource src)
        {
            Attach(src);
        }
        else
        {
            _window.SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(_window) is HwndSource s)
                    Attach(s);
            };
        }
    }

    private void Attach(HwndSource source)
    {
        _source = source;
        _hwnd = source.Handle;
        source.AddHook(WndProc);
    }

    public void Register(uint modifiers, Key key, Action handler)
    {
        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_hwnd, id, modifiers, vk))
        {
            Trace.Log("hotkey", $"RegisterHotKey failed: mod={modifiers}, key={key}");
            return;
        }
        _handlers[id] = handler;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            try { handler(); } catch (Exception ex) { Trace.Log("hotkey", $"handler threw: {ex.Message}"); }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys)
        {
            try { UnregisterHotKey(_hwnd, id); } catch { }
        }
        _handlers.Clear();
    }
}
