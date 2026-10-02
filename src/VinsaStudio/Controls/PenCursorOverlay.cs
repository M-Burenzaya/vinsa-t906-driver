using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VinsaStudio.Controls;

public sealed class PenCursorOverlay : FrameworkElement
{
    private Point position;
    private double? tiltX,tiltY;
    public PenCursorOverlay() { IsHitTestVisible=false;ClipToBounds=true;Visibility=Visibility.Collapsed; }

    public bool Update(StylusPoint point)
    {
        position=new Point(point.X,point.Y);
        tiltX=Angle(point,StylusPointProperties.XTiltOrientation);
        tiltY=Angle(point,StylusPointProperties.YTiltOrientation);
        Visibility=Visibility.Visible;InvalidateVisual();
        return tiltX.HasValue && tiltY.HasValue;
    }

    private static double? Angle(StylusPoint point,StylusPointProperty property)
    {
        if(!point.HasProperty(property)) return null;
        var info=point.Description.GetPropertyInfo(property);
        if(info.Resolution<=0 || !float.IsFinite(info.Resolution)) return null;
        double value=point.GetPropertyValue(property)/info.Resolution;
        if(info.Unit==StylusPointPropertyUnit.Radians) value=value*180/Math.PI;
        else if(info.Unit!=StylusPointPropertyUnit.Degrees) return null;
        return double.IsFinite(value)?Math.Clamp(value,-90,90):null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if(tiltX.HasValue && tiltY.HasValue)
        {
            var end=position+new Vector(Math.Sin(tiltX.Value*Math.PI/180)*42,Math.Sin(tiltY.Value*Math.PI/180)*42);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(24,24,24)),7),position,end);
            dc.DrawLine(new Pen(Brushes.Silver,4),position,end);
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(24,24,24)),new Pen(Brushes.White,1),position,3,3);
    }
}
