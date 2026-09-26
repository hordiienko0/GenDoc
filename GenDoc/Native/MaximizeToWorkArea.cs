using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GenDoc.Native;

/// <summary>
/// Вікно без системної рамки (WindowStyle=None + WindowChrome) Windows розгортає на ВЕСЬ монітор - разом
/// із панеллю задач, і низ застосунку (статусний рядок, «Далі →», легенда) ховався під нею. Обробка
/// WM_GETMINMAXINFO обмежує розгорнуте вікно робочою областю того монітора, де воно стоїть.
/// </summary>
public static class MaximizeToWorkArea
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
        };
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var work = info.Work;
        var bounds = info.Monitor;

        minMax.MaxPosition.X = work.Left - bounds.Left;
        minMax.MaxPosition.Y = work.Top - bounds.Top;
        minMax.MaxSize.X = work.Right - work.Left;
        minMax.MaxSize.Y = work.Bottom - work.Top;

        Marshal.StructureToPtr(minMax, lParam, true);
        // handled лишається false: WPF далі сам застосує MinWidth/MinHeight вікна.
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
