using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using Weather.Core;
using Weather.Geo;
using Weather.Rendering.Map;

namespace Weather.App.Controls;

/// <summary>
/// 世界地図(Rendering.Map)。1 本指でパン、2 本指でピンチズーム、タップで地点を選ぶ(TapCommand に GeoPoint を渡す)。
/// </summary>
public sealed class MapView:SKCanvasView,IDisposable{
    public static readonly BindableProperty MarkersProperty=BindableProperty.Create(nameof(Markers),typeof(IReadOnlyList<MapMarker>),typeof(MapView),null,propertyChanged:Redraw);
    public static readonly BindableProperty TapCommandProperty=BindableProperty.Create(nameof(TapCommand),typeof(ICommand),typeof(MapView));

    private readonly WorldMapRenderer map;
    private readonly Dictionary<long,SKPoint> touches=[];
    private SKPoint pressedAt;
    private bool moved;
    private float lastPinchDistance;

    public MapView(){
        var database=IPlatformApplication.Current?.Services.GetService<GeoDatabase>()??GeoDatabase.LoadEmbedded();
        this.map=new WorldMapRenderer(database);
        this.EnableTouchEvents=true;
        this.PaintSurface+=this.OnPaint;
        this.Touch+=this.OnTouch;
    }

    public IReadOnlyList<MapMarker>? Markers{
        get=>(IReadOnlyList<MapMarker>?)this.GetValue(MarkersProperty);
        set=>this.SetValue(MarkersProperty,value);
    }

    public ICommand? TapCommand{
        get=>(ICommand?)this.GetValue(TapCommandProperty);
        set=>this.SetValue(TapCommandProperty,value);
    }

    public void ZoomBy(double factor){
        var size=this.CanvasSize;
        this.map.Viewport.ZoomBy(factor,new SKPoint(size.Width/2,size.Height/2),size);
        this.InvalidateSurface();
    }

    public void CenterOn(GeoPoint point,double zoom){
        this.map.Viewport.CenterLatitude=point.Latitude;
        this.map.Viewport.CenterLongitude=point.Longitude;
        this.map.Viewport.Zoom=zoom;
        this.InvalidateSurface();
    }

    private static void Redraw(BindableObject bindable,object? oldValue,object? newValue){
        ((MapView)bindable).InvalidateSurface();
    }

    private void OnPaint(object? sender,SKPaintSurfaceEventArgs e){
        this.map.Draw(e.Surface.Canvas,new SKSize(e.Info.Width,e.Info.Height),this.Markers??[]);
    }

    private void OnTouch(object? sender,SKTouchEventArgs e){
        var size=this.CanvasSize;
        switch(e.ActionType){
            case SKTouchAction.Pressed:
                this.touches[e.Id]=e.Location;
                if(this.touches.Count==1){
                    this.pressedAt=e.Location;
                    this.moved=false;
                }
                this.lastPinchDistance=this.PinchDistance();
                break;
            case SKTouchAction.Moved:
                if(!this.touches.TryGetValue(e.Id,out var previous)){
                    break;
                }
                this.touches[e.Id]=e.Location;
                if(this.touches.Count>=2){
                    var distance=this.PinchDistance();
                    if(this.lastPinchDistance>0&&distance>0){
                        var center=this.PinchCenter();
                        this.map.Viewport.ZoomBy(distance/this.lastPinchDistance,center,size);
                    }
                    this.lastPinchDistance=distance;
                    this.moved=true;
                }else{
                    this.map.Viewport.Pan(e.Location.X-previous.X,e.Location.Y-previous.Y,size);
                    if(SKPoint.Distance(e.Location,this.pressedAt)>12){
                        this.moved=true;
                    }
                }
                this.InvalidateSurface();
                break;
            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                var wasSingle=this.touches.Count==1;
                this.touches.Remove(e.Id);
                if(e.ActionType==SKTouchAction.Released&&wasSingle&&!this.moved){
                    var point=this.map.HitTest(e.Location,size);
                    if(this.TapCommand?.CanExecute(point)==true){
                        this.TapCommand.Execute(point);
                    }
                }
                this.lastPinchDistance=this.PinchDistance();
                break;
            case SKTouchAction.WheelChanged:
                this.map.Viewport.ZoomBy(Math.Pow(1.1,e.WheelDelta/120.0),e.Location,size);
                this.InvalidateSurface();
                break;
        }
        e.Handled=true;
    }

    private float PinchDistance(){
        if(this.touches.Count<2){
            return 0;
        }
        var points=this.touches.Values.Take(2).ToArray();
        return SKPoint.Distance(points[0],points[1]);
    }

    private SKPoint PinchCenter(){
        var points=this.touches.Values.Take(2).ToArray();
        return new SKPoint((points[0].X+points[1].X)/2,(points[0].Y+points[1].Y)/2);
    }

    public void Dispose(){
        this.map.Dispose();
    }
}
