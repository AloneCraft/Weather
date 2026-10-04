using System.Diagnostics;
using SkiaSharp;
using Weather.Presentation;
using Weather.Rendering;
using Weather.Rendering.Scene;
using Weather.Scene;
#if !IOS
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
#endif

namespace Weather.App.Controls;

/// <summary>描画ループの共通部分(SceneRenderer・時計・設定の反映)。</summary>
public sealed class SceneHost:IDisposable{
    private readonly Stopwatch clock=Stopwatch.StartNew();

    public SceneRenderer Renderer{get;}=new();

    public void Render(SKCanvas canvas,SKSizeI size){
        this.Renderer.Render(canvas,size,this.clock.Elapsed.TotalSeconds);
    }

    public void Apply(AppSettings? settings,IMotionPreferences? motion){
        var options=this.Renderer.Options;
        options.ReduceMotion=motion?.ReduceMotion??false;
        if(settings is null){
            return;
        }
        options.ReduceFlashes=settings.ReduceFlashes||options.ReduceMotion;
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
    }

    public void Dispose(){
        this.Renderer.Dispose();
    }
}

/// <summary>
/// シーンの描画ビュー(Rendering.md「描画ホストとフレームループ」)。
/// Android は SKGLView(連続描画)、iOS は SKMetalView の自作ハンドラー。非表示・背景では停止する。
/// 省電力設定では 30 fps に下げる。
/// </summary>
public sealed class SceneView:ContentView,IDisposable{
    public static readonly BindableProperty SceneProperty=BindableProperty.Create(nameof(Scene),typeof(SceneState),typeof(SceneView),null,propertyChanged:OnSceneChanged);
    public static readonly BindableProperty IsRunningProperty=BindableProperty.Create(nameof(IsRunning),typeof(bool),typeof(SceneView),false,propertyChanged:OnRunningChanged);

    private readonly SceneHost host=new();
#if IOS
    private readonly SceneMetalView canvas;
#else
    private readonly SKGLView canvas;
    private IDispatcherTimer? timer;
#endif

    public SceneView(){
#if IOS
        this.canvas=new SceneMetalView(this.host);
#else
        this.canvas=new SKGLView{HasRenderLoop=false,IgnorePixelScaling=false};
        this.canvas.PaintSurface+=this.OnPaintSurface;
#endif
        this.Content=this.canvas;
        this.InputTransparent=true;
    }

    public SceneState? Scene{
        get=>(SceneState?)this.GetValue(SceneProperty);
        set=>this.SetValue(SceneProperty,value);
    }

    public bool IsRunning{
        get=>(bool)this.GetValue(IsRunningProperty);
        set=>this.SetValue(IsRunningProperty,value);
    }

    private static AppSettings? Settings=>IPlatformApplication.Current?.Services.GetService<AppSettings>();
    private static IMotionPreferences? Motion=>IPlatformApplication.Current?.Services.GetService<IMotionPreferences>();

    private static void OnSceneChanged(BindableObject bindable,object? oldValue,object? newValue){
        var view=(SceneView)bindable;
        if(newValue is SceneState state){
            var first=oldValue is null;
            view.host.Renderer.SetTarget(state);
            if(first){
                view.host.Renderer.SnapToTarget();
            }
        }
    }

    private static void OnRunningChanged(BindableObject bindable,object? oldValue,object? newValue){
        ((SceneView)bindable).UpdateLoop();
    }

    private void UpdateLoop(){
        this.host.Apply(Settings,Motion);
        var powerSaving=Settings?.PowerSaving??false;
        var fps=60;
        if(powerSaving||this.host.Renderer.Options.ReduceMotion){
            fps=30;
        }
#if IOS
        this.canvas.FramesPerSecond=fps;
        this.canvas.IsRunning=this.IsRunning;
#else
        this.timer?.Stop();
        this.canvas.HasRenderLoop=false;
        if(!this.IsRunning){
            return;
        }
        if(fps>=60){
            this.canvas.HasRenderLoop=true;
            return;
        }
        this.timer??=this.Dispatcher.CreateTimer();
        this.timer.Interval=TimeSpan.FromMilliseconds(1000.0/fps);
        this.timer.Tick-=this.OnTick;
        this.timer.Tick+=this.OnTick;
        this.timer.Start();
#endif
    }

#if !IOS
    private void OnTick(object? sender,EventArgs e){
        this.canvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender,SKPaintGLSurfaceEventArgs e){
        this.host.Render(e.Surface.Canvas,e.BackendRenderTarget.Size);
    }
#endif

    public void Dispose(){
#if !IOS
        this.timer?.Stop();
        this.canvas.PaintSurface-=this.OnPaintSurface;
#endif
        this.host.Dispose();
    }
}
