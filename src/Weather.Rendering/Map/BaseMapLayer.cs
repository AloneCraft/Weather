using SkiaSharp;
using Weather.Core;
using Weather.Geo;
using Weather.Geo.Data;
using Weather.Geo.Spatial;

namespace Weather.Rendering.Map;

/// <summary>
/// 背景の地図(自前のベクター。Natural Earth の国・気象庁の市町村境界・GeoNames の地名。オフライン)。
/// 陸地は色の層の下、海岸線・境界・地名は色の層の上に描く(Windy と同じ重ね方)。
/// 経緯度のパスは Web メルカトルの世界の座標で一度だけ作り、ズームに応じて 3 段階に簡略化したものを使う。
/// </summary>
internal sealed class BaseMapLayer(GeoDatabase database):IDisposable{
    public static readonly SKColor Sea=new(0x10,0x1a,0x2b);
    public static readonly SKColor Land=new(0x26,0x2e,0x38);

    private static readonly double[] Tolerances=[0.08,0.02,0];
    private readonly SKPaint land=new(){IsAntialias=true,Color=Land};
    private readonly SKPaint border=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.White.WithAlpha(110)};
    private readonly SKPaint area=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.White.WithAlpha(45)};
    private readonly SKPaint text=new(){IsAntialias=true,Color=SKColors.White.WithAlpha(225)};
    private readonly SKPaint halo=new(){IsAntialias=true,Color=SKColors.Black.WithAlpha(150),Style=SKPaintStyle.Stroke,StrokeJoin=SKStrokeJoin.Round};
    private readonly List<SKRect> used=[];
    private (SKPath Path,SKRect Bounds)[][]? countries;
    private (SKPath Path,SKRect Bounds)[]? areas;
    private SKFont? font;

    public void DrawLand(SKCanvas canvas,MapView view,SKSize size){
        canvas.Clear(Sea);
        var paths=this.Countries(view.Zoom);
        foreach(var wrap in Wraps(view,size)){
            canvas.Save();
            canvas.Concat(view.WorldToScreen(size,wrap));
            var visible=Visible(view,size,wrap);
            foreach(var (path,bounds) in paths){
                if(bounds.IntersectsWith(visible)){
                    canvas.DrawPath(path,this.land);
                }
            }
            canvas.Restore();
        }
    }

    public void DrawLines(SKCanvas canvas,MapView view,SKSize size){
        var paths=this.Countries(view.Zoom);
        var s=(float)view.WorldSize;
        this.border.StrokeWidth=0.9f*view.PixelRatio/s;
        this.area.StrokeWidth=0.7f*view.PixelRatio/s;
        foreach(var wrap in Wraps(view,size)){
            canvas.Save();
            canvas.Concat(view.WorldToScreen(size,wrap));
            var visible=Visible(view,size,wrap);
            foreach(var (path,bounds) in paths){
                if(bounds.IntersectsWith(visible)){
                    canvas.DrawPath(path,this.border);
                }
            }
            //市町村境界はズーム 7 以上(気象庁の区域。日本周辺の表示単位)
            if(view.Zoom>=7){
                this.areas??=[..database.JmaAreas.Select(static a=>Build(a.Shape,0))];
                foreach(var (path,bounds) in this.areas){
                    if(bounds.IntersectsWith(visible)){
                        canvas.DrawPath(path,this.area);
                    }
                }
            }
            canvas.Restore();
        }
    }

    /// <summary>主要都市名(ズームに応じて人口のしきい値を下げ、重なりは間引く)。</summary>
    public void DrawLabels(SKCanvas canvas,MapView view,SKSize size){
        var scale=view.PixelRatio;
        this.font??=FontProvider.Create(12);
        this.font.Size=12*scale;
        this.halo.StrokeWidth=3*scale;
        var threshold=Math.Max(15_000,(long)(40_000_000/Math.Pow(2.6,view.Zoom)));
        this.used.Clear();
        foreach(var place in database.Places){
            if(place.Population<threshold){
                break;
            }
            var p=view.ToScreen(place.Latitude,place.Longitude,size);
            if(p.X<0||p.Y<0||p.X>size.Width||p.Y>size.Height){
                continue;
            }
            var label=place.JaName??place.Name;
            var width=this.font.MeasureText(label,this.text);
            var rect=new SKRect(p.X-width/2-4*scale,p.Y-16*scale,p.X+width/2+4*scale,p.Y+4*scale);
            if(this.Overlaps(rect)){
                continue;
            }
            this.used.Add(rect);
            canvas.DrawCircle(p,2*scale,this.text);
            canvas.DrawText(label,p.X,p.Y-5*scale,SKTextAlign.Center,this.font,this.halo);
            canvas.DrawText(label,p.X,p.Y-5*scale,SKTextAlign.Center,this.font,this.text);
            if(this.used.Count>45){
                break;
            }
        }
    }

    private bool Overlaps(SKRect rect){
        foreach(var r in this.used){
            if(r.IntersectsWith(rect)){
                return true;
            }
        }
        return false;
    }

    /// <summary>表示範囲に入る経度方向の複製(−1・0・1)。</summary>
    private static IEnumerable<int> Wraps(MapView view,SKSize size){
        var (ox,_)=view.Origin(size);
        var span=size.Width/view.WorldSize;
        for(var wrap=-1;wrap<=1;wrap++){
            if(wrap+1>=ox&&wrap<=ox+span){
                yield return wrap;
            }
        }
    }

    /// <summary>複製 wrap の座標での表示範囲(世界の座標)。</summary>
    private static SKRect Visible(MapView view,SKSize size,int wrap){
        var (ox,oy)=view.Origin(size);
        var s=view.WorldSize;
        return new SKRect((float)(ox-wrap),(float)oy,(float)(ox-wrap+size.Width/s),(float)(oy+size.Height/s));
    }

    private (SKPath Path,SKRect Bounds)[] Countries(double zoom){
        this.countries??=[..Tolerances.Select(t=>database.Countries.Select(c=>Build(c.Shape,t)).ToArray())];
        if(zoom<4){
            return this.countries[0];
        }
        if(zoom<7){
            return this.countries[1];
        }
        return this.countries[2];
    }

    /// <summary>経緯度の多角形(リングは緯度・経度の順)→ 世界の座標のパス。</summary>
    private static (SKPath Path,SKRect Bounds) Build(GeoShape shape,double tolerance){
        using var builder=new SKPathBuilder{FillType=SKPathFillType.EvenOdd};
        foreach(var source in shape.Rings){
            var ring=source;
            if(tolerance>0){
                ring=Polygons.Simplify(source,tolerance);
            }
            if(ring.Length<6){
                continue;
            }
            var (x0,y0)=MapCamera.Project(ring[0],ring[1]);
            builder.MoveTo((float)x0,(float)y0);
            for(var i=2;i+1<ring.Length;i+=2){
                var (x,y)=MapCamera.Project(ring[i],ring[i+1]);
                builder.LineTo((float)x,(float)y);
            }
            builder.Close();
        }
        var path=builder.Detach();
        return (path,path.Bounds);
    }

    public void Dispose(){
        foreach(var level in this.countries??[]){
            foreach(var (path,_) in level){
                path.Dispose();
            }
        }
        foreach(var (path,_) in this.areas??[]){
            path.Dispose();
        }
        this.land.Dispose();
        this.border.Dispose();
        this.area.Dispose();
        this.text.Dispose();
        this.halo.Dispose();
        this.font?.Dispose();
    }
}
