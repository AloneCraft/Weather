using System.Diagnostics;
using SkiaSharp;
using Weather.Core;
using Weather.Geo;
using Weather.Presentation;
using Weather.Presentation.ViewModels;
using Weather.Rendering;
using Weather.Rendering.Map;

namespace Weather.App.Controls;

/// <summary>地図の描画(MapRenderer・時計)。</summary>
public sealed class MapHost(MapRenderer renderer):IFrameRenderer{
    private readonly Stopwatch clock=Stopwatch.StartNew();

    public MapRenderer Renderer{get;}=renderer;

    public void Render(SKCanvas canvas,SKSizeI size,GRRecordingContext? context){
        this.Renderer.Render(canvas,size,this.clock.Elapsed.TotalSeconds,context);
    }
}

/// <summary>
/// 地図(Rendering.Map)。1 本指でパン、2 本指でピンチ、ダブルタップで拡大、タップで地点(またはピン)を選ぶ。
/// 風の粒子があるときだけ連続描画し、それ以外は操作・データの更新のたびに 1 回描く。
/// </summary>
public sealed class WeatherMapView:ContentView,IDisposable{
    public static readonly BindableProperty MapFrameProperty=BindableProperty.Create(nameof(MapFrame),typeof(MapFrame),typeof(WeatherMapView),null,propertyChanged:OnFrameChanged);
    public static readonly BindableProperty PinsProperty=BindableProperty.Create(nameof(Pins),typeof(IReadOnlyList<MapPinItem>),typeof(WeatherMapView),null,propertyChanged:OnPinsChanged);
    public static readonly BindableProperty SelectedPointProperty=BindableProperty.Create(nameof(SelectedPoint),typeof(GeoPoint?),typeof(WeatherMapView),null,propertyChanged:OnSelectedChanged);

    private readonly MapRenderer renderer;
    private readonly AnimatedCanvas canvas;
    private double panX;
    private double panY;
    private bool active;

    public WeatherMapView(){
        var services=IPlatformApplication.Current?.Services;
        var database=services?.GetService<GeoDatabase>()??GeoDatabase.LoadEmbedded();
        this.renderer=new MapRenderer(database);
        if(services?.GetService<IMapDataService>() is {} maps){
            this.renderer.TileLoader=(layer,z,x,y,ct)=>maps.GetTileAsync(layer,z,x,y,ct).AsTask();
        }
        this.renderer.RedrawRequested+=()=>MainThread.BeginInvokeOnMainThread(this.Redraw);
        this.renderer.Camera.PixelRatio=(float)Math.Max(1,DeviceDisplay.Current.MainDisplayInfo.Density);
        this.canvas=new AnimatedCanvas(new MapHost(this.renderer));
        this.Content=this.canvas;

        var pan=new PanGestureRecognizer();
        pan.PanUpdated+=this.OnPan;
        var pinch=new PinchGestureRecognizer();
        pinch.PinchUpdated+=this.OnPinch;
        var tap=new TapGestureRecognizer();
        tap.Tapped+=this.OnTap;
        var doubleTap=new TapGestureRecognizer{NumberOfTapsRequired=2};
        doubleTap.Tapped+=this.OnDoubleTap;
        this.GestureRecognizers.Add(pan);
        this.GestureRecognizers.Add(pinch);
        this.GestureRecognizers.Add(doubleTap);
        this.GestureRecognizers.Add(tap);
    }

    /// <summary>地図の地点のタップ(ピン以外)。</summary>
    public event EventHandler<GeoPoint>? PointTapped;

    public event EventHandler<MapPinItem>? PinTapped;

    public MapFrame? MapFrame{
        get=>(MapFrame?)this.GetValue(MapFrameProperty);
        set=>this.SetValue(MapFrameProperty,value);
    }

    public IReadOnlyList<MapPinItem>? Pins{
        get=>(IReadOnlyList<MapPinItem>?)this.GetValue(PinsProperty);
        set=>this.SetValue(PinsProperty,value);
    }

    public GeoPoint? SelectedPoint{
        get=>(GeoPoint?)this.GetValue(SelectedPointProperty);
        set=>this.SetValue(SelectedPointProperty,value);
    }

    /// <summary>画面に表示されているか(非表示・背景では描画ループを止める)。</summary>
    public void SetActive(bool value,AppSettings? settings,IMotionPreferences? motion){
        this.active=value;
        var options=this.renderer.Options;
        options.ReduceMotion=motion?.ReduceMotion??false;
        if(settings is not null){
            switch(settings.Quality){
                case RenderQuality.Low:
                    options.AutoTier=false;
                    options.Tier=QualityTier.Low;
                    break;
                case RenderQuality.Medium:
                    options.AutoTier=false;
                    options.Tier=QualityTier.Medium;
                    break;
                case RenderQuality.High:
                    options.AutoTier=false;
                    options.Tier=QualityTier.High;
                    break;
                default:
                    options.AutoTier=true;
                    break;
            }
            this.canvas.FramesPerSecond=60;
            if(settings.PowerSaving){
                this.canvas.FramesPerSecond=30;
            }
        }
        this.UpdateLoop();
    }

    public void FocusOn(GeoPoint point,double? zoom=null){
        this.renderer.Camera.CenterOn(point,zoom);
        this.Redraw();
    }

    private void UpdateLoop(){
        this.canvas.IsRunning=this.active&&this.renderer.NeedsAnimation;
        this.Redraw();
    }

    private void Redraw(){
        if(!this.canvas.IsRunning){
            this.canvas.Invalidate();
        }
    }

    private static void OnFrameChanged(BindableObject bindable,object? oldValue,object? newValue){
        var view=(WeatherMapView)bindable;
        view.renderer.Frame=newValue as MapFrame;
        view.UpdateLoop();
    }

    private static void OnPinsChanged(BindableObject bindable,object? oldValue,object? newValue){
        var view=(WeatherMapView)bindable;
        var pins=new List<MapPin>();
        if(newValue is IReadOnlyList<MapPinItem> items){
            foreach(var item in items){
                pins.Add(new MapPin(item.Point,item.Label,false));
            }
        }
        view.renderer.Pins=pins;
        view.Redraw();
    }

    private static void OnSelectedChanged(BindableObject bindable,object? oldValue,object? newValue){
        var view=(WeatherMapView)bindable;
        view.renderer.Selected=newValue as GeoPoint?;
        view.Redraw();
    }

    private float Density=>this.renderer.Camera.PixelRatio;

    private SKSize PixelSize=>new((float)(this.Width*this.Density),(float)(this.Height*this.Density));

    private void OnPan(object? sender,PanUpdatedEventArgs e){
        switch(e.StatusType){
            case GestureStatus.Started:
                this.panX=0;
                this.panY=0;
                break;
            case GestureStatus.Running:
                var dx=e.TotalX-this.panX;
                var dy=e.TotalY-this.panY;
                this.panX=e.TotalX;
                this.panY=e.TotalY;
                this.renderer.Camera.Pan(dx*this.Density,dy*this.Density);
                this.Redraw();
                break;
        }
    }

    private void OnPinch(object? sender,PinchGestureUpdatedEventArgs e){
        if(e.Status!=GestureStatus.Running){
            return;
        }
        var size=this.PixelSize;
        var focus=new SKPoint((float)(e.ScaleOrigin.X*size.Width),(float)(e.ScaleOrigin.Y*size.Height));
        this.renderer.Camera.ZoomBy(e.Scale,focus,size);
        this.Redraw();
    }

    private void OnDoubleTap(object? sender,TappedEventArgs e){
        if(e.GetPosition(this) is not {} p){
            return;
        }
        this.renderer.Camera.ZoomBy(2,new SKPoint((float)(p.X*this.Density),(float)(p.Y*this.Density)),this.PixelSize);
        this.Redraw();
    }

    private void OnTap(object? sender,TappedEventArgs e){
        if(e.GetPosition(this) is not {} p){
            return;
        }
        var size=this.PixelSize;
        var point=new SKPoint((float)(p.X*this.Density),(float)(p.Y*this.Density));
        var view=this.renderer.Camera.Snapshot();
        //ピン(地点の上に吹き出しの形で描く)を先に判定する
        foreach(var pin in this.Pins??[]){
            var s=view.ToScreen(pin.Point.Latitude,pin.Point.Longitude,size);
            if(Math.Abs(point.X-s.X)<44*this.Density&&point.Y>s.Y-36*this.Density&&point.Y<s.Y+10*this.Density){
                this.PinTapped?.Invoke(this,pin);
                return;
            }
        }
        this.PointTapped?.Invoke(this,view.ToGeo(point,size));
    }

    public void Dispose(){
        this.canvas.Stop();
        this.renderer.Dispose();
    }
}
