using System;

namespace EveFocus;

internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Native.WinEventProc _callback;
    private readonly IntPtr _foregroundHook;
    private readonly IntPtr _restoreHook;

    public event Action? Changed;

    public ForegroundWatcher()
    {
        _callback = OnEvent;
        _foregroundHook = Hook(Native.EVENT_SYSTEM_FOREGROUND);
        _restoreHook = Hook(Native.EVENT_SYSTEM_MINIMIZEEND);
    }

    private IntPtr Hook(uint eventType) =>
        Native.SetWinEventHook(eventType, eventType, IntPtr.Zero, _callback, 0, 0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        Changed?.Invoke();

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
        if (_restoreHook != IntPtr.Zero) Native.UnhookWinEvent(_restoreHook);
    }
}
