using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VinsaStudio.Controls;

public sealed record DesktopMonitor(string Name, int X, int Y, int Width, int Height, bool Primary);
public sealed record DesktopSnapshot(IReadOnlyList<DesktopMonitor> Monitors, int X, int Y, int Width, int Height, BitmapSource? Image);

public static class DesktopCapture
{
    public static DesktopSnapshot Read(bool capture=true)
    {
        var monitors=new List<DesktopMonitor>();
        MonitorCallback callback=(IntPtr monitor,IntPtr dc,ref NativeRect rect,IntPtr data)=>
        {
            var info=new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>(), Device="" };
            if (GetMonitorInfoW(monitor,ref info)) monitors.Add(new(info.Device,info.Area.Left,info.Area.Top,
                info.Area.Right-info.Area.Left,info.Area.Bottom-info.Area.Top,(info.Flags&1)!=0));
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero) || monitors.Count==0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not read monitor layout.");
        monitors=monitors.OrderBy(m=>m.X).ThenBy(m=>m.Y).ToList();
        int x=monitors.Min(m=>m.X), y=monitors.Min(m=>m.Y);
        int width=monitors.Max(m=>m.X+m.Width)-x, height=monitors.Max(m=>m.Y+m.Height)-y;
        BitmapSource? image=null;
        if (capture)
        {
            double scale=Math.Min(1,Math.Min(2560.0/width,1440.0/height));
            int w=Math.Max(1,(int)Math.Round(width*scale)), h=Math.Max(1,(int)Math.Round(height*scale));
            IntPtr screen=GetDC(IntPtr.Zero), memory=IntPtr.Zero, bitmap=IntPtr.Zero, old=IntPtr.Zero;
            try
            {
                if (screen==IntPtr.Zero) throw new Win32Exception();
                memory=CreateCompatibleDC(screen); bitmap=CreateCompatibleBitmap(screen,w,h);
                if (memory==IntPtr.Zero || bitmap==IntPtr.Zero) throw new Win32Exception();
                old=SelectObject(memory,bitmap);
                if (old==IntPtr.Zero || old==new IntPtr(-1)) throw new Win32Exception();
                SetStretchBltMode(memory,4);
                if (!StretchBlt(memory,0,0,w,h,screen,x,y,width,height,0x40CC0020)) throw new Win32Exception();
                var captured=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                // GDI screen bitmaps have undefined alpha; treat captured pixels as opaque.
                image=new FormatConvertedBitmap(captured,PixelFormats.Bgr32,null,0);
                image.Freeze();
            }
            finally
            {
                if (old!=IntPtr.Zero && old!=new IntPtr(-1) && memory!=IntPtr.Zero) SelectObject(memory,old);
                if (bitmap!=IntPtr.Zero) DeleteObject(bitmap);
                if (memory!=IntPtr.Zero) DeleteDC(memory);
                if (screen!=IntPtr.Zero) ReleaseDC(IntPtr.Zero,screen);
            }
        }
        return new(monitors,x,y,width,height,image);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct MonitorInfo
    { public int Size; public NativeRect Area,Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device; }
    private delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref NativeRect rect,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc,int mode);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool StretchBlt(IntPtr dest,int x,int y,int w,int h,IntPtr source,int sx,int sy,int sw,int sh,uint operation);
}
