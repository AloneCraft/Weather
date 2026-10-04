using SkiaSharp;
using Weather.Core;

namespace Weather.Rendering.Map;

/// <summary>気象庁のタイルの、ある地点の画素(吹き出しで凡例の区分を引くため)。</summary>
public static class JmaTileSampler{
    /// <summary>地点を含むタイルと、タイル内の画素の位置。読むズームは画像が実在する範囲で、細かすぎない 8 程度にする。</summary>
    public static (int Z,int X,int Y,int PixelX,int PixelY) Locate(JmaTileLayer layer,GeoPoint point,double zoom=8){
        ArgumentNullException.ThrowIfNull(layer);
        var z=layer.TileZoomFor(zoom);
        var n=1<<z;
        var (x,y)=MapCamera.Project(point.Latitude,point.Longitude);
        var fx=x*n;
        var fy=y*n;
        var tx=Math.Clamp((int)Math.Floor(fx),0,n-1);
        var ty=Math.Clamp((int)Math.Floor(fy),0,n-1);
        var px=Math.Clamp((int)((fx-tx)*256),0,255);
        var py=Math.Clamp((int)((fy-ty)*256),0,255);
        return (z,tx,ty,px,py);
    }

    /// <summary>PNG の画素(ARGB)。読めなければ null。</summary>
    public static uint? Pixel(byte[] png,int x,int y){
        ArgumentNullException.ThrowIfNull(png);
        using var bitmap=SKBitmap.Decode(png);
        if(bitmap is null||x<0||y<0||x>=bitmap.Width||y>=bitmap.Height){
            return null;
        }
        return (uint)bitmap.GetPixel(x,y);
    }
}
