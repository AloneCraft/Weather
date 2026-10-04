using SkiaSharp;
#if !IOS
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
#endif

namespace Weather.App.Controls;

/// <summary>描画の中身(空のシーン・地図)。描画スレッドから呼ばれる。</summary>
public interface IFrameRenderer{
    void Render(SKCanvas canvas,SKSizeI size,GRRecordingContext? context);
}

/// <summary>
/// 連続描画のホスト(Rendering.md「描画ホストとフレームループ」)。
/// Android は SKGLView、iOS は SKMetalView の自作ハンドラー。IsRunning の間は連続描画し、止めている間は Invalidate() で 1 回だけ描く。
/// 省電力では 30 fps。
/// </summary>
public sealed class AnimatedCanvas:ContentView{
    public static readonly BindableProperty IsRunningProperty=BindableProperty.Create(nameof(IsRunning),typeof(bool),typeof(AnimatedCanvas),false,propertyChanged:OnLoopChanged);
    public static readonly BindableProperty FramesPerSecondProperty=BindableProperty.Create(nameof(FramesPerSecond),typeof(int),typeof(AnimatedCanvas),60,propertyChanged:OnLoopChanged);

#if IOS
    private readonly MetalCanvasView canvas;
#else
    private readonly SKGLView canvas;
    private IDispatcherTimer? timer;
#endif

    public AnimatedCanvas(IFrameRenderer renderer){
        ArgumentNullException.ThrowIfNull(renderer);
        this.Renderer=renderer;
#if IOS
        this.canvas=new MetalCanvasView(renderer);
#else
        this.canvas=new SKGLView{HasRenderLoop=false,IgnorePixelScaling=false};
        this.canvas.PaintSurface+=this.OnPaintSurface;
#endif
        this.Content=this.canvas;
    }

    public IFrameRenderer Renderer{get;}

    public bool IsRunning{
        get=>(bool)this.GetValue(IsRunningProperty);
        set=>this.SetValue(IsRunningProperty,value);
    }

    public int FramesPerSecond{
        get=>(int)this.GetValue(FramesPerSecondProperty);
        set=>this.SetValue(FramesPerSecondProperty,value);
    }

    /// <summary>止めている間に 1 回だけ描き直す(地図の操作・データの更新)。UI のスレッドから呼ぶ。</summary>
    public void Invalidate(){
#if IOS
        this.canvas.RequestRedraw();
#else
        this.canvas.InvalidateSurface();
#endif
    }

    private static void OnLoopChanged(BindableObject bindable,object? oldValue,object? newValue){
        ((AnimatedCanvas)bindable).UpdateLoop();
    }

    private void UpdateLoop(){
#if IOS
        this.canvas.FramesPerSecond=this.FramesPerSecond;
        this.canvas.IsRunning=this.IsRunning;
#else
        this.timer?.Stop();
        this.canvas.HasRenderLoop=false;
        if(!this.IsRunning){
            return;
        }
        if(this.FramesPerSecond>=60){
            this.canvas.HasRenderLoop=true;
            return;
        }
        this.timer??=this.Dispatcher.CreateTimer();
        this.timer.Interval=TimeSpan.FromMilliseconds(1000.0/this.FramesPerSecond);
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
        this.Renderer.Render(e.Surface.Canvas,e.BackendRenderTarget.Size,e.Surface.Context);
    }
#endif

    public void Stop(){
        this.IsRunning=false;
#if !IOS
        this.timer?.Stop();
#endif
    }
}
