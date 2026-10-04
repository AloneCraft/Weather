using System.Globalization;
using SkiaSharp;

namespace Weather.Rendering.Charts;

/// <summary>グラフの 1 点。値は表示単位に換算済み(換算は Presentation の責務)。</summary>
public readonly record struct ChartSample(DateTimeOffset Time,double? Temperature,double? Precipitation,int? Probability,bool Suspect=false);

public sealed class ChartStyle{
    public TimeZoneInfo Zone{get;init;}=TimeZoneInfo.Utc;
    public bool DarkBackground{get;init;}=true;
    public string TemperatureFormat{get;init;}="{0:0}°";
    public string PrecipitationUnit{get;init;}="mm";
    public float FontSize{get;init;}=12;
    public bool ShowProbability{get;init;}=true;
    public string HourFormat{get;init;}="H'時'";
    public string DateFormat{get;init;}="M/d";

    public SKColor Text{
        get{
            if(this.DarkBackground){
                return SKColors.White.WithAlpha(220);
            }
            return new SKColor(0x20,0x24,0x2c);
        }
    }
}

/// <summary>
/// 時系列グラフ(Rendering.md「グラフ」)。気温の滑らかな折れ線、降水量の棒、降水確率の面。
/// 時間軸は地点のタイムゾーンで描き、タップ位置から時刻を返す(シーンの時間軸と連動)。
/// </summary>
public sealed class TimeSeriesChart:IDisposable{
    private static readonly SKColor TemperatureColor=new(0xff,0xa6,0x4d);
    private static readonly SKColor PrecipitationColor=new(0x5a,0xa9,0xff);
    private static readonly SKColor ProbabilityColor=new(0x5a,0xa9,0xff,0x40);
    private readonly SKPaint line=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=2.5f,StrokeCap=SKStrokeCap.Round,Color=TemperatureColor};
    private readonly SKPaint fill=new(){IsAntialias=true};
    private readonly SKPaint text=new(){IsAntialias=true};
    private readonly SKPaint grid=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=1};

    public void Draw(SKCanvas canvas,SKRect bounds,IReadOnlyList<ChartSample> samples,ChartStyle style,DateTimeOffset? cursor=null){
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(style);
        if(samples.Count<2||bounds.Width<10||bounds.Height<10){
            return;
        }
        using var font=FontProvider.Create(style.FontSize);
        var axisHeight=style.FontSize*1.8f;
        var plot=new SKRect(bounds.Left,bounds.Top+style.FontSize*1.6f,bounds.Right,bounds.Bottom-axisHeight);
        var start=samples[0].Time;
        var end=samples[^1].Time;
        var span=(end-start).TotalSeconds;
        if(span<=0){
            return;
        }
        float X(DateTimeOffset t){
            return plot.Left+(float)((t-start).TotalSeconds/span)*plot.Width;
        }

        this.grid.Color=style.Text.WithAlpha(40);
        this.text.Color=style.Text;
        //日付の区切りと時刻の目盛り(現地時刻)
        var lastLabelX=float.MinValue;
        var step=Math.Max(1,(int)Math.Ceiling(samples.Count/8.0));
        for(var i=0;i<samples.Count;i++){
            var local=TimeZoneInfo.ConvertTime(samples[i].Time,style.Zone);
            var x=X(samples[i].Time);
            if(local.Hour==0&&local.Minute==0){
                canvas.DrawLine(x,plot.Top,x,plot.Bottom,this.grid);
                canvas.DrawText(local.ToString(style.DateFormat,CultureInfo.InvariantCulture),x+3,bounds.Bottom-2,SKTextAlign.Left,font,this.text);
                lastLabelX=x+40;
            }else if(i%step==0&&x>lastLabelX){
                canvas.DrawText(local.ToString(style.HourFormat,CultureInfo.InvariantCulture),x,bounds.Bottom-2,SKTextAlign.Center,font,this.text);
                lastLabelX=x+28;
            }
        }

        //降水確率の面(下 40%)
        if(style.ShowProbability){
            using var builder=new SKPathBuilder();
            var started=false;
            foreach(var s in samples){
                if(s.Probability is not {} p){
                    continue;
                }
                var x=X(s.Time);
                var y=plot.Bottom-plot.Height*0.4f*p/100f;
                if(!started){
                    builder.MoveTo(x,plot.Bottom);
                    started=true;
                }
                builder.LineTo(x,y);
            }
            if(started){
                builder.LineTo(X(samples[^1].Time),plot.Bottom);
                builder.Close();
                using var path=builder.Detach();
                this.fill.Color=ProbabilityColor;
                canvas.DrawPath(path,this.fill);
            }
        }

        //降水量の棒(5 mm で上限の 40%、それを超えたら頭打ちの印)
        var barWidth=Math.Max(2,plot.Width/samples.Count*0.6f);
        this.fill.Color=PrecipitationColor;
        foreach(var s in samples){
            if(s.Precipitation is not {} mm||mm<=0){
                continue;
            }
            var h=(float)Math.Min(1,mm/5)*plot.Height*0.4f;
            var x=X(s.Time);
            canvas.DrawRect(x-barWidth/2,plot.Bottom-h,barWidth,h,this.fill);
        }

        //気温の折れ線(Catmull-Rom を 3 次ベジェに変換)
        var temps=samples.Where(static s=>s.Temperature is not null).ToList();
        if(temps.Count>=2){
            var min=temps.Min(static s=>s.Temperature!.Value);
            var max=temps.Max(static s=>s.Temperature!.Value);
            if(max-min<4){
                var mid=(max+min)/2;
                min=mid-2;
                max=mid+2;
            }
            var top=plot.Top+plot.Height*0.05f;
            var bottom=plot.Top+plot.Height*0.58f;
            SKPoint P(ChartSample s){
                var y=bottom-(float)((s.Temperature!.Value-min)/(max-min))*(bottom-top);
                return new SKPoint(X(s.Time),y);
            }
            using var builder=new SKPathBuilder();
            var points=temps.Select(P).ToList();
            builder.MoveTo(points[0]);
            for(var i=0;i+1<points.Count;i++){
                var p0=points[Math.Max(0,i-1)];
                var p1=points[i];
                var p2=points[i+1];
                var p3=points[Math.Min(points.Count-1,i+2)];
                var c1=new SKPoint(p1.X+(p2.X-p0.X)/6,p1.Y+(p2.Y-p0.Y)/6);
                var c2=new SKPoint(p2.X-(p3.X-p1.X)/6,p2.Y-(p3.Y-p1.Y)/6);
                builder.CubicTo(c1,c2,p2);
            }
            using var path=builder.Detach();
            canvas.DrawPath(path,this.line);
            //最高・最低のラベル
            var hi=temps.MaxBy(static s=>s.Temperature!.Value);
            var lo=temps.MinBy(static s=>s.Temperature!.Value);
            var hp=P(hi);
            var lp=P(lo);
            canvas.DrawText(string.Format(CultureInfo.InvariantCulture,style.TemperatureFormat,hi.Temperature),hp.X,hp.Y-6,SKTextAlign.Center,font,this.text);
            canvas.DrawText(string.Format(CultureInfo.InvariantCulture,style.TemperatureFormat,lo.Temperature),lp.X,lp.Y+style.FontSize+4,SKTextAlign.Center,font,this.text);
            //準正常値の印(気象庁サイトと同様に ")")
            foreach(var s in temps.Where(static s=>s.Suspect)){
                var sp=P(s);
                canvas.DrawText(")",sp.X+3,sp.Y,SKTextAlign.Left,font,this.text);
            }
        }

        if(cursor is {} c&&c>=start&&c<=end){
            var x=X(c);
            this.grid.Color=style.Text.WithAlpha(160);
            canvas.DrawLine(x,plot.Top,x,plot.Bottom,this.grid);
        }
    }

    /// <summary>x 座標 → 時刻(グラフのドラッグでシーンの時刻を動かす)。</summary>
    public static DateTimeOffset? HitTest(SKRect bounds,IReadOnlyList<ChartSample> samples,float x){
        ArgumentNullException.ThrowIfNull(samples);
        if(samples.Count<2||bounds.Width<=0){
            return null;
        }
        var t=Math.Clamp((x-bounds.Left)/bounds.Width,0,1);
        var start=samples[0].Time;
        var end=samples[^1].Time;
        return start+TimeSpan.FromSeconds((end-start).TotalSeconds*t);
    }

    public void Dispose(){
        this.line.Dispose();
        this.fill.Dispose();
        this.text.Dispose();
        this.grid.Dispose();
    }
}
