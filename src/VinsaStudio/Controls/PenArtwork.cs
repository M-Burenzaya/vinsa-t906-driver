using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VinsaStudio.Controls;

internal static class PenArtwork
{
    public static BitmapSource Load()
    {
        var source=new BitmapImage(new Uri("pack://application:,,,/Assets/PenShape.bmp"));
        var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int width=converted.PixelWidth,height=converted.PixelHeight,stride=width*4;
        var pixels=new byte[stride*height];
        converted.CopyPixels(pixels,stride,0);
        var pending=new Queue<int>();
        void Visit(int x,int y)
        {
            int offset=y*stride+x*4;
            if(pixels[offset+3]==0 || Math.Abs(pixels[offset]-240)>10
                || Math.Abs(pixels[offset+1]-240)>10 || Math.Abs(pixels[offset+2]-240)>10) return;
            pixels[offset+3]=0; pending.Enqueue(y*width+x);
        }
        // Only remove the flat background connected to the image edges. Preserve
        // highlights inside the pen; the original embedded BMP stays unchanged.
        for(int x=0;x<width;x++) { Visit(x,0);Visit(x,height-1); }
        for(int y=0;y<height;y++) { Visit(0,y);Visit(width-1,y); }
        while(pending.TryDequeue(out int point))
        {
            int x=point%width,y=point/width;
            if(x>0) Visit(x-1,y); if(x+1<width) Visit(x+1,y);
            if(y>0) Visit(x,y-1); if(y+1<height) Visit(x,y+1);
        }
        // Display the original artwork in the UI's monochrome palette.
        for(int offset=0;offset<pixels.Length;offset+=4)
        {
            byte gray=(byte)((pixels[offset]*114+pixels[offset+1]*587+pixels[offset+2]*299)/1000);
            pixels[offset]=pixels[offset+1]=pixels[offset+2]=gray;
        }
        var result=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,stride);
        result.Freeze();return result;
    }
}
