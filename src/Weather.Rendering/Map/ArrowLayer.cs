using System.Globalization;
using SkiaSharp;
using Weather.Core;

namespace Weather.Rendering.Map;

/// <summary>
/// 日本周辺の観測・予報の点(アメダスの風の矢印と気温、海上分布予報の風向)。値はそのまま描き、補間しない(方針 5)。
/// 重なる点は画面の格子で間引く。
/// </summary>
internal sealed class ArrowLayer:IDisposable{
    private readonly SKPaint stroke=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint outline=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeCap=SKStrokeCap.Round,Color=SKColors.Black.WithAlpha(140)};
    private readonly SKPaint fill=new(){IsAntialias=true};
    private readonly SKPaint text=new(){IsAntialias=true,Color=SKColors.White};
    private readonly SKPaint halo=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.Black.WithAlpha(170)};
    private readonly HashSet<long> occupied=[];
    private SKFont? font;

    public void DrawArrows(SKCanvas canvas,MapView view,SKSize size,WindArrowSet set){
        var scale=view.PixelRatio;
        var spacing=Math.Max(14,24*scale);
        this.occupied.Clear();
        this.stroke.StrokeWidth=1.8f*scale;
        this.outline.StrokeWidth=3.4f*scale;
        foreach(var arrow in set.Arrows){
            var p=view.ToScreen(arrow.Point.Latitude,arrow.Point.Longitude,size);
            if(p.X<-20||p.Y<-20||p.X>size.Width+20||p.Y>size.Height+20||!this.Reserve(p,spacing)){
                continue;
            }
            var length=16f*scale;
            var color=SKColors.White;
            if(arrow.SpeedMs is {} speed){
                length=(float)Math.Clamp(8+speed*1.6,10,30)*scale;
                color=MapPalettes.ColorFor(MapLegends.WindSpeed,speed).WithAlpha(255);
            }
            //矢印は風が吹いていく向き(吹いてくる方位 + 180°)。画面は北が上
            var toward=(arrow.FromDirectionDeg+180)*Math.PI/180;
            var dx=(float)Math.Sin(toward);
            var dy=(float)-Math.Cos(toward);
            var tail=new SKPoint(p.X-dx*length/2,p.Y-dy*length/2);
            var head=new SKPoint(p.X+dx*length/2,p.Y+dy*length/2);
            var wing=length*0.35f;
            var left=new SKPoint(head.X-(dx*0.8f+dy*0.6f)*wing,head.Y-(dy*0.8f-dx*0.6f)*wing);
            var right=new SKPoint(head.X-(dx*0.8f-dy*0.6f)*wing,head.Y-(dy*0.8f+dx*0.6f)*wing);
            this.stroke.Color=color;
            DrawArrow(canvas,tail,head,left,right,this.outline);
            DrawArrow(canvas,tail,head,left,right,this.stroke);
        }
    }

    private static void DrawArrow(SKCanvas canvas,SKPoint tail,SKPoint head,SKPoint left,SKPoint right,SKPaint paint){
        canvas.DrawLine(tail,head,paint);
        canvas.DrawLine(head,left,paint);
        canvas.DrawLine(head,right,paint);
    }

    /// <summary>気温の点(凡例の色の丸。ズーム 7 以上は値も出す)。</summary>
    public void DrawPoints(SKCanvas canvas,MapView view,SKSize size,PointValueSet set,Legend legend){
        var scale=view.PixelRatio;
        var showText=view.Zoom>=7;
        var spacing=12*scale;
        if(showText){
            spacing=30*scale;
        }
        this.font??=FontProvider.Create(11);
        this.font.Size=11*scale;
        this.halo.StrokeWidth=2.5f*scale;
        this.outline.StrokeWidth=1.2f*scale;
        this.occupied.Clear();
        foreach(var point in set.Points){
            var p=view.ToScreen(point.Point.Latitude,point.Point.Longitude,size);
            if(p.X<0||p.Y<0||p.X>size.Width||p.Y>size.Height||!this.Reserve(p,spacing)){
                continue;
            }
            this.fill.Color=MapPalettes.ColorFor(legend,point.Value).WithAlpha(255);
            canvas.DrawCircle(p,4.5f*scale,this.fill);
            canvas.DrawCircle(p,4.5f*scale,this.outline);
            if(showText){
                var label=point.Value.ToString("0.0",CultureInfo.InvariantCulture);
                canvas.DrawText(label,p.X,p.Y-7*scale,SKTextAlign.Center,this.font,this.halo);
                canvas.DrawText(label,p.X,p.Y-7*scale,SKTextAlign.Center,this.font,this.text);
            }
        }
        this.outline.StrokeWidth=3.4f*scale;
    }

    /// <summary>画面の格子の 1 マスに 1 点だけ置く。</summary>
    private bool Reserve(SKPoint p,float spacing){
        var key=((long)Math.Floor(p.X/spacing)<<32)|(uint)(int)Math.Floor(p.Y/spacing);
        return this.occupied.Add(key);
    }

    public void Dispose(){
        this.stroke.Dispose();
        this.outline.Dispose();
        this.fill.Dispose();
        this.text.Dispose();
        this.halo.Dispose();
        this.font?.Dispose();
    }
}
