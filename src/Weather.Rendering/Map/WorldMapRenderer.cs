using SkiaSharp;
using Weather.Core;
using Weather.Geo;
using Weather.Geo.Data;

namespace Weather.Rendering.Map;

public readonly record struct MapMarker(GeoPoint Point,string Label,bool Selected);

/// <summary>地図の表示範囲(正距円筒図法)。Zoom 0 で経度 360° が画面幅。</summary>
public sealed class MapViewport{
    public const double MaxZoom=9;

    public double CenterLatitude{get;set;}=35;
    public double CenterLongitude{get;set;}=139;
    public double Zoom{get;set;}=2;

    public float Scale(SKSize size){
        return (float)(size.Width/360d*Math.Pow(2,this.Zoom));
    }

    public SKPoint ToScreen(double lat,double lon,SKSize size){
        var scale=this.Scale(size);
        var dLon=lon-this.CenterLongitude;
        if(dLon>180){
            dLon-=360;
        }else if(dLon<-180){
            dLon+=360;
        }
        return new SKPoint((float)(size.Width/2+dLon*scale),(float)(size.Height/2+(this.CenterLatitude-lat)*scale));
    }

    public GeoPoint ToGeo(SKPoint point,SKSize size){
        var scale=this.Scale(size);
        var lon=this.CenterLongitude+(point.X-size.Width/2)/scale;
        var lat=this.CenterLatitude-(point.Y-size.Height/2)/scale;
        while(lon>180){
            lon-=360;
        }
        while(lon<-180){
            lon+=360;
        }
        return new GeoPoint(Math.Clamp(lat,-90,90),lon);
    }

    public void Pan(float dx,float dy,SKSize size){
        var scale=this.Scale(size);
        this.CenterLongitude-=dx/scale;
        this.CenterLatitude=Math.Clamp(this.CenterLatitude+dy/scale,-85,85);
        while(this.CenterLongitude>180){
            this.CenterLongitude-=360;
        }
        while(this.CenterLongitude<-180){
            this.CenterLongitude+=360;
        }
    }

    /// <summary>焦点の地理座標を保ったまま拡大縮小する。</summary>
    public void ZoomBy(double factor,SKPoint focus,SKSize size){
        var before=this.ToGeo(focus,size);
        this.Zoom=Math.Clamp(this.Zoom+Math.Log2(factor),0,MaxZoom);
        var after=this.ToGeo(focus,size);
        this.CenterLongitude+=before.Longitude-after.Longitude;
        this.CenterLatitude=Math.Clamp(this.CenterLatitude+before.Latitude-after.Latitude,-85,85);
    }
}

/// <summary>
/// 世界地図(Rendering.md「世界地図」)。Natural Earth の国境と気象庁の区域を Geo のデータから描く(オフライン)。
/// 経緯度のパスを一度だけ作り、表示はキャンバスの行列で拡大縮小する。
/// </summary>
public sealed class WorldMapRenderer(GeoDatabase database):IDisposable{
    private readonly SKPaint land=new(){IsAntialias=true,Color=new SKColor(0x2a,0x3a,0x3a)};
    private readonly SKPaint border=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=new SKColor(0x7a,0x8a,0x90,0x90)};
    private readonly SKPaint area=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=new SKColor(0x9a,0xb0,0xc0,0x60)};
    private readonly SKPaint text=new(){IsAntialias=true,Color=SKColors.White.WithAlpha(210)};
    private readonly SKPaint marker=new(){IsAntialias=true};
    private List<SKPath>? countryPaths;
    private List<(JmaAreaRecord Area,SKPath Path)>? areaPaths;

    public MapViewport Viewport{get;}=new();

    public void Draw(SKCanvas canvas,SKSize size,IReadOnlyList<MapMarker> markers){
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(markers);
        canvas.Clear(new SKColor(0x0d,0x1b,0x2a));
        this.countryPaths??=[..database.Countries.Select(static c=>ToPath(c.Shape))];
        var scale=this.Viewport.Scale(size);
        //経度方向の繰り返し(日付変更線をまたぐ表示)
        for(var wrap=-1;wrap<=1;wrap++){
            canvas.Save();
            var origin=this.Viewport.ToScreen(0,0,size);
            canvas.Translate(origin.X+wrap*360*scale,origin.Y);
            canvas.Scale(scale,-scale);
            this.border.StrokeWidth=1/scale;
            foreach(var path in this.countryPaths){
                canvas.DrawPath(path,this.land);
                canvas.DrawPath(path,this.border);
            }
            if(this.Viewport.Zoom>=4.5&&wrap==0){
                this.areaPaths??=[..database.JmaAreas.Select(static a=>(a,ToPath(a.Shape)))];
                this.area.StrokeWidth=0.8f/scale;
                foreach(var (_,path) in this.areaPaths){
                    canvas.DrawPath(path,this.area);
                }
            }
            canvas.Restore();
        }
        this.DrawLabels(canvas,size);
        foreach(var m in markers){
            var p=this.Viewport.ToScreen(m.Point.Latitude,m.Point.Longitude,size);
            this.marker.Color=new SKColor(0xff,0xa6,0x4d);
            if(m.Selected){
                this.marker.Color=new SKColor(0xff,0xe0,0x66);
            }
            canvas.DrawCircle(p,7,this.marker);
        }
    }

    public GeoPoint HitTest(SKPoint point,SKSize size){
        return this.Viewport.ToGeo(point,size);
    }

    /// <summary>ズームに応じて人口のしきい値を変え、主要都市名を表示する(重なりは間引く)。</summary>
    private void DrawLabels(SKCanvas canvas,SKSize size){
        var threshold=Math.Max(30_000,(long)(20_000_000/Math.Pow(2.4,this.Viewport.Zoom)));
        using var font=FontProvider.Create(12);
        var used=new List<SKRect>();
        foreach(var place in database.Places){
            if(place.Population<threshold){
                break;
            }
            var p=this.Viewport.ToScreen(place.Latitude,place.Longitude,size);
            if(p.X<0||p.Y<0||p.X>size.Width||p.Y>size.Height){
                continue;
            }
            var label=place.JaName??place.Name;
            var width=font.MeasureText(label,this.text);
            var rect=new SKRect(p.X-width/2-4,p.Y-16,p.X+width/2+4,p.Y+4);
            if(used.Any(r=>r.IntersectsWith(rect))){
                continue;
            }
            used.Add(rect);
            canvas.DrawCircle(p,2,this.text);
            canvas.DrawText(label,p.X,p.Y-5,SKTextAlign.Center,font,this.text);
            if(used.Count>60){
                break;
            }
        }
    }

    /// <summary>経緯度(x = 経度、y = 緯度)のパス。表示時に y を反転する。</summary>
    private static SKPath ToPath(GeoShape shape){
        using var builder=new SKPathBuilder{FillType=SKPathFillType.EvenOdd};
        foreach(var ring in shape.Rings){
            if(ring.Length<6){
                continue;
            }
            builder.MoveTo(ring[1],ring[0]);
            for(var i=2;i+1<ring.Length;i+=2){
                builder.LineTo(ring[i+1],ring[i]);
            }
            builder.Close();
        }
        return builder.Detach();
    }

    public void Dispose(){
        this.land.Dispose();
        this.border.Dispose();
        this.area.Dispose();
        this.text.Dispose();
        this.marker.Dispose();
        foreach(var path in this.countryPaths??[]){
            path.Dispose();
        }
        foreach(var (_,path) in this.areaPaths??[]){
            path.Dispose();
        }
    }
}
