using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VinsaStudio.Core;

namespace VinsaStudio.Controls;

/// <summary>Desktop/tablet preview with selection movement and eight resize handles.</summary>
public sealed class MappingSurface : FrameworkElement
{
    private static readonly Brush SelectionOutline=new SolidColorBrush(Color.FromRgb(214,214,214));
    private AreaRect area=AreaRect.Full, startArea=AreaRect.Full;
    private AreaHandle drag;
    private Point start;
    private Rect active;
    private DesktopSnapshot? desktop;
    public bool IsTablet { get; set; }
    public AreaRect Area { get=>area; set { area=value; InvalidateVisual(); } }
    public DesktopSnapshot? Desktop { get=>desktop; set { desktop=value; InvalidateVisual(); } }
    public event Action<AreaRect>? AreaChanged;
    public MappingSurface() { Focusable=true; ClipToBounds=true; MinHeight=140; }
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(30,30,30)),null,new Rect(0,0,ActualWidth,ActualHeight),0,0);
        if (ActualWidth<80 || ActualHeight<80) return;
        // Illustration proportions match the original UI; these are not physical dimensions.
        double ratio=desktop is { Width:>0,Height:>0 } ? desktop.Width/(double)desktop.Height : 16.0/9;
        double spaceW=ActualWidth-46, spaceH=ActualHeight-68;
        double width=Math.Min(spaceW,spaceH*ratio),height=width/ratio;
        active=new Rect((ActualWidth-width)/2,(ActualHeight-height)/2-12,width,height);
        if (IsTablet)
        {
            // Match the supplied original silhouette: 510x340 body, 385x260
            // writing surface, and five unsegmented key bars. These are UI units.
            double scale=Math.Min(ActualWidth/841,ActualHeight/346);
            Rect body=new((ActualWidth-510*scale)/2+5*scale,(ActualHeight-340*scale)/2,510*scale,340*scale);
            active=new Rect(body.Left+86*scale,body.Top+40*scale,385*scale,260*scale);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(44,44,44)),new Pen(new SolidColorBrush(Color.FromRgb(83,83,83)),1),body,10*scale,10*scale);
            for(int i=0;i<5;i++)
            {
                Rect key=new(body.Left+20*scale,body.Top+(60+i*24)*scale,49*scale,19*scale);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(80,80,80)),new Pen(new SolidColorBrush(Color.FromRgb(24,24,24)),1),key);
            }
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(62,62,62)),new Pen(new SolidColorBrush(Color.FromRgb(83,83,83)),1),active,3,3);
        }
        else
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(53,53,53)),null,new Rect(active.Left-8,active.Top-8,active.Width+16,active.Height+16),7,7);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10,10,10)),null,active);
            if(desktop?.Image is {} image) dc.DrawImage(image,active);
            if(desktop is not null)
                for(int i=0;i<desktop.Monitors.Count;i++)
                {
                    var m=desktop.Monitors[i];
                    Rect box=new(active.Left+(m.X-desktop.X)/(double)desktop.Width*active.Width,
                        active.Top+(m.Y-desktop.Y)/(double)desktop.Height*active.Height,
                        m.Width/(double)desktop.Width*active.Width,m.Height/(double)desktop.Height*active.Height);
                    dc.DrawRectangle(null,new Pen(new SolidColorBrush(Color.FromRgb(139,139,139)),1),box);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225,27,27,27)),null,new Rect(box.Left+5,box.Top+5,95,23),4,4);
                    Text(dc,$"Display {i+1}"+(m.Primary?" · main":""),box.Left+12,box.Top+9,10,Brushes.White);
                }
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(79,79,79)),null,new Rect(active.Left+active.Width*.44,active.Bottom+8,active.Width*.12,12));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(79,79,79)),null,new Rect(active.Left+active.Width*.33,active.Bottom+20,active.Width*.34,5),2,2);
        }
        Rect selection=Selection();
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(20,255,255,255)),null,selection);
        dc.PushClip(new RectangleGeometry(selection));
        for(double x=selection.Left-selection.Height;x<selection.Right;x+=10)
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(75,205,205,205)),.8),new Point(x,selection.Bottom),new Point(x+selection.Height,selection.Top));
        dc.Pop();
        dc.DrawRectangle(null,new Pen(SelectionOutline,2),selection);
        foreach(var (point,handle) in Handles(selection))
            dc.DrawRectangle(SelectionOutline,new Pen(new SolidColorBrush(Color.FromRgb(36,36,36)),1),new Rect(point.X-4,point.Y-4,8,8));
        if(IsKeyboardFocused) dc.DrawRoundedRectangle(null,new Pen(SelectionOutline,1),new Rect(1,1,ActualWidth-2,ActualHeight-2),10,10);
    }
    private void Text(DrawingContext dc,string value,double x,double y,double size,Brush brush)
    {
        var text=new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new Point(x,y));
    }
    private Rect Selection()=>new(active.Left+area.Left/4095.0*active.Width,active.Top+area.Top/4095.0*active.Height,
        (area.Right-area.Left)/4095.0*active.Width,(area.Bottom-area.Top)/4095.0*active.Height);
    private static IEnumerable<(Point,AreaHandle)> Handles(Rect r)
    {
        yield return(new(r.Left,r.Top),AreaHandle.Left|AreaHandle.Top);
        yield return(new(r.Right,r.Top),AreaHandle.Right|AreaHandle.Top);
        yield return(new(r.Left,r.Bottom),AreaHandle.Left|AreaHandle.Bottom);
        yield return(new(r.Right,r.Bottom),AreaHandle.Right|AreaHandle.Bottom);
        yield return(new((r.Left+r.Right)/2,r.Top),AreaHandle.Top);
        yield return(new((r.Left+r.Right)/2,r.Bottom),AreaHandle.Bottom);
        yield return(new(r.Left,(r.Top+r.Bottom)/2),AreaHandle.Left);
        yield return(new(r.Right,(r.Top+r.Bottom)/2),AreaHandle.Right);
    }
    private AreaHandle Hit(Point p)
    {
        foreach(var (point,handle) in Handles(Selection())) if(Math.Abs(p.X-point.X)<=9 && Math.Abs(p.Y-point.Y)<=9) return handle;
        return Selection().Contains(p)?AreaHandle.Move:AreaHandle.Create;
    }
    private static Cursor HandleCursor(AreaHandle handle)=>handle switch
    {
        AreaHandle.Left or AreaHandle.Right=>Cursors.SizeWE,
        AreaHandle.Top or AreaHandle.Bottom=>Cursors.SizeNS,
        AreaHandle.Left|AreaHandle.Top or AreaHandle.Right|AreaHandle.Bottom=>Cursors.SizeNWSE,
        AreaHandle.Left|AreaHandle.Bottom or AreaHandle.Right|AreaHandle.Top=>Cursors.SizeNESW,
        AreaHandle.Move=>Cursors.SizeAll,_=>Cursors.Cross
    };
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.ChangedButton!=MouseButton.Left) return;
        var point=e.GetPosition(this);
        if(!new Rect(active.Left-10,active.Top-10,active.Width+20,active.Height+20).Contains(point)) return;
        Focus();start=point;startArea=area;drag=Hit(point);CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point=e.GetPosition(this);
        Cursor=HandleCursor(drag!=AreaHandle.None?drag:Hit(point));
        if(drag==AreaHandle.None || e.LeftButton!=MouseButtonState.Pressed || active.Width<=0 || active.Height<=0) return;
        AreaRect result;
        if(drag==AreaHandle.Create)
        {
            int x1=Coord(start.X,active.Left,active.Width),y1=Coord(start.Y,active.Top,active.Height);
            int x2=Coord(point.X,active.Left,active.Width),y2=Coord(point.Y,active.Top,active.Height);
            if(x1==x2 || y1==y2) return;
            result=new(Math.Min(x1,x2),Math.Min(y1,y2),Math.Max(x1,x2),Math.Max(y1,y2));
        }
        else result=MappingGeometry.Drag(startArea,drag,(int)Math.Round((point.X-start.X)/active.Width*4095),(int)Math.Round((point.Y-start.Y)/active.Height*4095));
        SetArea(result);e.Handled=true;
    }
    private static int Coord(double p,double origin,double extent)=>(int)Math.Clamp(Math.Round((p-origin)/extent*4095),0,4095);
    private void SetArea(AreaRect value) { area=value;InvalidateVisual();AreaChanged?.Invoke(value); }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);if(e.ChangedButton!=MouseButton.Left) return;
        drag=AreaHandle.None;ReleaseMouseCapture();e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e);drag=AreaHandle.None; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Escape && drag!=AreaHandle.None) { SetArea(startArea);ReleaseMouseCapture();e.Handled=true;return; }
        int step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?16:1;
        int dx=e.Key==Key.Left?-step:e.Key==Key.Right?step:0,dy=e.Key==Key.Up?-step:e.Key==Key.Down?step:0;
        if(dx!=0 || dy!=0) { SetArea(MappingGeometry.Drag(area,AreaHandle.Move,dx,dy));e.Handled=true; }
    }
}
