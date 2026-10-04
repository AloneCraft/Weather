using System.Diagnostics;
using SkiaSharp;
using Weather.Presentation;
using Weather.Rendering;
using Weather.Rendering.Scene;
using Weather.Scene;

namespace Weather.App.Controls;

/// <summary>空のシーンの描画(SceneRenderer・時計・設定の反映)。</summary>
public sealed class SceneHost:IFrameRenderer,IDisposable{
    private readonly Stopwatch clock=Stopwatch.StartNew();

    public SceneRenderer Renderer{get;}=new();

    public void Render(SKCanvas canvas,SKSizeI size,GRRecordingContext? context){
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
/// 空のシーンの描画ビュー(地点の詳細画面の背景)。非表示・背景では停止する。省電力設定では 30 fps に下げる。
/// </summary>
public sealed class SceneView:ContentView,IDisposable{
    public static readonly BindableProperty SceneProperty=BindableProperty.Create(nameof(Scene),typeof(SceneState),typeof(SceneView),null,propertyChanged:OnSceneChanged);
    public static readonly BindableProperty IsRunningProperty=BindableProperty.Create(nameof(IsRunning),typeof(bool),typeof(SceneView),false,propertyChanged:OnRunningChanged);

    private readonly SceneHost host=new();
    private readonly AnimatedCanvas canvas;

    public SceneView(){
        this.canvas=new AnimatedCanvas(this.host);
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
        var view=(SceneView)bindable;
        view.host.Apply(Settings,Motion);
        var fps=60;
        if(Settings?.PowerSaving==true||view.host.Renderer.Options.ReduceMotion){
            fps=30;
        }
        view.canvas.FramesPerSecond=fps;
        view.canvas.IsRunning=view.IsRunning;
    }

    public void Dispose(){
        this.canvas.Stop();
        this.host.Dispose();
    }
}
