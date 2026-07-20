using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GenDoc.Native;

public static class DwmHelper
{
    private const int DwmwaUseImmersiveDarkMode20 = 20;
    private const int DwmwaUseImmersiveDarkMode19 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void EnableDarkTitleBar(Window window)
    {
        if (PresentationSource.FromVisual(window) is not null)
        {
            Apply(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply(window);
        }
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var useDarkMode = 1;
        var result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode20, ref useDarkMode, sizeof(int));
        if (result != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode19, ref useDarkMode, sizeof(int));
        }
    }
}
