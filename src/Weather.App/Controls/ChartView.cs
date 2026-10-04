using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using Weather.Presentation.ViewModels;
using Weather.Rendering.Charts;

namespace Weather.App.Controls;

/// <summary>時系列グラフ(Rendering.Charts)。ドラッグ位置の時刻を Cursor に書き戻す(シーンの時間軸と連動)。</summary>
public sealed class ChartView:SKCanvasView,IDisposable{
    public static readonly BindableProperty PointsProperty=BindableProperty.Create(nameof(Points),typeof(IReadOnlyList<ChartPoint>),typeof(ChartView),null,propertyChanged:Redraw);
    public static readonly BindableProperty ZoneProperty=BindableProperty.Create(nameof(Zone),typeof(TimeZoneInfo),typeof(ChartView),TimeZoneInfo.Utc,propertyChanged:Redraw);
    public static readonly BindableProperty CursorProperty=BindableProperty.Create(nameof(Cursor),typeof(DateTimeOffset?),typeof(ChartView),null,BindingMode.TwoWay,propertyChanged:Redraw);
    public static readonly BindableProperty ShowProbabilityProperty=BindableProperty.Create(nameof(ShowProbability),typeof(bool),typeof(ChartView),true,propertyChanged:Redraw);

    private readonly TimeSeriesChart chart=new();
    private ChartSample[] samples=[];

    public ChartView(){
        this.EnableTouchEvents=true;
        this.PaintSurface+=this.OnPaint;
        this.Touch+=this.OnTouch;
    }

    public IReadOnlyList<ChartPoint>? Points{
        get=>(IReadOnlyList<ChartPoint>?)this.GetValue(PointsProperty);
        set=>this.SetValue(PointsProperty,value);
    }

    public TimeZoneInfo Zone{
        get=>(TimeZoneInfo)this.GetValue(ZoneProperty);
        set=>this.SetValue(ZoneProperty,value);
    }

    public DateTimeOffset? Cursor{
        get=>(DateTimeOffset?)this.GetValue(CursorProperty);
        set=>this.SetValue(CursorProperty,value);
    }

    public bool ShowProbability{
        get=>(bool)this.GetValue(ShowProbabilityProperty);
        set=>this.SetValue(ShowProbabilityProperty,value);
    }

    private static void Redraw(BindableObject bindable,object? oldValue,object? newValue){
        var view=(ChartView)bindable;
        if(ReferenceEquals(newValue,view.Points)||newValue is IReadOnlyList<ChartPoint>){
            view.samples=[..(view.Points??[]).Select(static p=>new ChartSample(p.Time,p.Temperature,p.Precipitation,p.Probability,p.Suspect))];
        }
        view.InvalidateSurface();
    }

    private void OnPaint(object? sender,SKPaintSurfaceEventArgs e){
        var canvas=e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var scale=e.Info.Width/(float)Math.Max(1,this.Width);
        this.chart.Draw(canvas,new SKRect(8*scale,4*scale,e.Info.Width-8*scale,e.Info.Height-4*scale),this.samples,new ChartStyle{Zone=this.Zone,FontSize=12*scale,ShowProbability=this.ShowProbability,HourFormat=Weather.Presentation.Resources.Strings.HourFormat},this.Cursor);
    }

    private void OnTouch(object? sender,SKTouchEventArgs e){
        if(e.ActionType is SKTouchAction.Pressed or SKTouchAction.Moved){
            var width=this.CanvasSize.Width;
            var scale=width/(float)Math.Max(1,this.Width);
            this.Cursor=TimeSeriesChart.HitTest(new SKRect(8*scale,0,width-8*scale,this.CanvasSize.Height),this.samples,e.Location.X);
            e.Handled=true;
        }
    }

    public void Dispose(){
        this.chart.Dispose();
    }
}
