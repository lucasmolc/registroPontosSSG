using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RegistroPontosSSG.Desktop;

/// <summary>
/// Aplica title bar escura (Windows 10 2004+ / Windows 11) via DWM.
/// </summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window)
    {
        void Set()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }

        if (window.IsLoaded) Set();
        else window.SourceInitialized += (_, _) => Set();
    }
}
