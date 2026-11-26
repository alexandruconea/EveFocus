using System;
using System.Runtime.InteropServices;

namespace EveFocus;

internal static class WindowOps
{
    public static void Activate(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Show(hwnd, Native.SW_RESTORE);

        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground == hwnd) return;

        uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        uint thisThread = Native.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread
                        && Responds(foreground)
                        && Native.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Native.AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    private static bool Responds(IntPtr hwnd) =>
        Native.SendMessageTimeout(hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG,
            500, out _) != IntPtr.Zero;

    public static void Minimize(IntPtr hwnd) => Show(hwnd, Native.SW_MINIMIZE);

    public static void MinimizeNoActivate(IntPtr hwnd) => Show(hwnd, Native.SW_SHOWMINNOACTIVE);

    public static void Restore(IntPtr hwnd) => Show(hwnd, Native.SW_RESTORE);

    public static WindowLayout GetLayout(IntPtr hwnd)
    {
        var placement = GetPlacement(hwnd);
        var r = placement.rcNormalPosition;
        bool maximized = placement.showCmd == Native.SW_SHOWMAXIMIZED
                         || (Native.IsIconic(hwnd) && (placement.flags & Native.WPF_RESTORETOMAXIMIZED) != 0);
        return new WindowLayout
        {
            X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top, Maximized = maximized,
        };
    }

    public static void ApplyLayout(IntPtr hwnd, WindowLayout layout)
    {
        if (layout.Width <= 0 || layout.Height <= 0 || Native.IsIconic(hwnd) || Native.IsHungAppWindow(hwnd)) return;

        var placement = GetPlacement(hwnd);
        placement.rcNormalPosition = new Native.RECT
        {
            Left = layout.X, Top = layout.Y, Right = layout.X + layout.Width, Bottom = layout.Y + layout.Height,
        };
        placement.showCmd = Native.SW_SHOWNOACTIVATE;
        Native.SetWindowPlacement(hwnd, ref placement);
        if (layout.Maximized) Show(hwnd, Native.SW_SHOWMAXIMIZED);
    }

    private static void Show(IntPtr hwnd, int command)
    {
        if (Native.IsHungAppWindow(hwnd)) Native.ShowWindowAsync(hwnd, command);
        else Native.ShowWindow(hwnd, command);
    }

    private static Native.WINDOWPLACEMENT GetPlacement(IntPtr hwnd)
    {
        var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        Native.GetWindowPlacement(hwnd, ref placement);
        return placement;
    }
}
