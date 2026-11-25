using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace EveFocus;

internal static class Hotkey
{
    public static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (string rawPart in text!.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart.Trim();
            if (part.Length == 0) continue;
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= Native.MOD_WIN; break;
                default:
                    if (key != 0) return false;
                    if (part.Length == 1 && part[0] >= '0' && part[0] <= '9')
                        key = (uint)Keys.D0 + (uint)(part[0] - '0');
                    else if (!part.All(char.IsDigit)
                             && Enum.TryParse(part, true, out Keys parsed)
                             && Enum.IsDefined(typeof(Keys), parsed)
                             && (parsed & Keys.Modifiers) == 0)
                        key = (uint)parsed;
                    else
                        return false;
                    break;
            }
        }
        return key != 0;
    }

    public static string Format(Keys keyData)
    {
        var parts = new List<string>();
        if ((keyData & Keys.Control) == Keys.Control) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) == Keys.Alt) parts.Add("Alt");
        if ((keyData & Keys.Shift) == Keys.Shift) parts.Add("Shift");
        Keys key = keyData & Keys.KeyCode;
        if (key is not (Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu))
            parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsAvailable(string combo)
    {
        const int ProbeId = 0xBFFE;
        if (!TryParse(combo, out uint modifiers, out uint key)) return false;
        if (!Native.RegisterHotKey(IntPtr.Zero, ProbeId, modifiers, key)) return false;
        Native.UnregisterHotKey(IntPtr.Zero, ProbeId);
        return true;
    }
}

internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int ReleaseTimeoutMs = 1000;

    private readonly Dictionary<int, Action> _actions = new();
    private readonly Timer _releaseTimer = new() { Interval = 10 };
    private Action? _pending;
    private int _pendingKey;
    private int _pendingSince;
    private int _nextId = 1;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
        _releaseTimer.Tick += (_, _) => RunPendingWhenReleased();
    }

    public bool Register(string combo, Action action, out string error)
    {
        if (!Hotkey.TryParse(combo, out uint modifiers, out uint key))
        {
            error = "not a valid key combination";
            return false;
        }
        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, modifiers | Native.MOD_NOREPEAT, key))
        {
            error = "already used by another program or by another EVE Focus hotkey";
            return false;
        }
        _actions[id] = action;
        error = "";
        return true;
    }

    public void UnregisterAll()
    {
        _releaseTimer.Stop();
        _pending = null;
        foreach (int id in _actions.Keys) Native.UnregisterHotKey(Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _actions.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            RunPending();
            _pending = action;
            _pendingKey = (int)((m.LParam.ToInt64() >> 16) & 0xFFFF);
            _pendingSince = Environment.TickCount;
            _releaseTimer.Start();
            return;
        }
        base.WndProc(ref m);
    }

    private void RunPendingWhenReleased()
    {
        bool held = Native.GetAsyncKeyState(_pendingKey) < 0;
        bool timedOut = unchecked(Environment.TickCount - _pendingSince) >= ReleaseTimeoutMs;
        if (held && !timedOut) return;
        RunPending();
    }

    private void RunPending()
    {
        _releaseTimer.Stop();
        var action = _pending;
        _pending = null;
        if (action == null) return;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        _releaseTimer.Dispose();
        DestroyHandle();
    }
}
