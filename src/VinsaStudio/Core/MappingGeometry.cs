namespace VinsaStudio.Core;

[Flags]
public enum AreaHandle { None=0, Left=1, Top=2, Right=4, Bottom=8, Move=16, Create=32 }

public static class MappingGeometry
{
    // Original T906 presets: toggle 0x4a85c0; model branch 0x4a645e..0x4a64b2.
    public static AreaRect TabletPreset(int index) => index switch
    {
        0 => AreaRect.Full,
        1 => new(650,650,3500,3500),
        2 => new(1024,1024,3071,3071),
        3 => new(0,148,4095,3948),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    public static AreaRect Drag(AreaRect original, AreaHandle handle, int dx, int dy)
    {
        int left=original.Left, top=original.Top, right=original.Right, bottom=original.Bottom;
        if (handle == AreaHandle.Move)
        {
            dx=Math.Clamp(dx,-left,4095-right); dy=Math.Clamp(dy,-top,4095-bottom);
            return new(left+dx,top+dy,right+dx,bottom+dy);
        }
        if (handle.HasFlag(AreaHandle.Left)) left=Math.Clamp(left+dx,0,right-1);
        if (handle.HasFlag(AreaHandle.Right)) right=Math.Clamp(right+dx,left+1,4095);
        if (handle.HasFlag(AreaHandle.Top)) top=Math.Clamp(top+dy,0,bottom-1);
        if (handle.HasFlag(AreaHandle.Bottom)) bottom=Math.Clamp(bottom+dy,top+1,4095);
        return new(left,top,right,bottom);
    }

    public static AreaRect MonitorArea(int x,int y,int width,int height,int desktopX,int desktopY,int desktopWidth,int desktopHeight)
    {
        if (width<=0 || height<=0 || desktopWidth<=0 || desktopHeight<=0)
            throw new ArgumentException("Monitor and desktop dimensions must be positive.");
        int left=Math.Clamp((int)Math.Round((x-desktopX)*4095.0/desktopWidth),0,4094);
        int top=Math.Clamp((int)Math.Round((y-desktopY)*4095.0/desktopHeight),0,4094);
        int right=Math.Clamp((int)Math.Round((x+width-desktopX)*4095.0/desktopWidth),left+1,4095);
        int bottom=Math.Clamp((int)Math.Round((y+height-desktopY)*4095.0/desktopHeight),top+1,4095);
        return new(left,top,right,bottom);
    }

}
