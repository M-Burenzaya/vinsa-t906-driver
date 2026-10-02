using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VinsaStudio.Controls;

internal static class NativeWindowTheme
{
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            // Ask DWM to theme the native frame; Windows still owns all caption buttons.
            int enabled=1;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,20,ref enabled,sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
