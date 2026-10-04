#if IOS
using Microsoft.Maui.Handlers;
using SkiaSharp;
using SkiaSharp.Views.iOS;

namespace Weather.App.Controls;

/// <summary>
/// iOS の Metal 描画ホスト(Rendering.md 決定事項)。MAUI 標準の SKGLView は iOS で OpenGL ES(非推奨)を使うため、
/// SkiaSharp.Views.iOS の SKMetalView(MTKView)を包む。連続描画は MTKView の PreferredFramesPerSecond、
/// 止めている間の描き直しは SetNeedsDisplay で行う。
/// </summary>
public sealed class MetalCanvasView(IFrameRenderer renderer):View{
    public static readonly BindableProperty IsRunningProperty=BindableProperty.Create(nameof(IsRunning),typeof(bool),typeof(MetalCanvasView),false);
    public static readonly BindableProperty FramesPerSecondProperty=BindableProperty.Create(nameof(FramesPerSecond),typeof(int),typeof(MetalCanvasView),60);

    public IFrameRenderer Renderer{get;}=renderer;

    public bool IsRunning{
        get=>(bool)this.GetValue(IsRunningProperty);
        set=>this.SetValue(IsRunningProperty,value);
    }

    public int FramesPerSecond{
        get=>(int)this.GetValue(FramesPerSecondProperty);
        set=>this.SetValue(FramesPerSecondProperty,value);
    }

    public void RequestRedraw(){
        this.Handler?.Invoke(nameof(this.RequestRedraw));
    }
}

public sealed class MetalCanvasViewHandler():ViewHandler<MetalCanvasView,SKMetalView>(Mapper,Commands){
    public static readonly IPropertyMapper<MetalCanvasView,MetalCanvasViewHandler> Mapper=new PropertyMapper<MetalCanvasView,MetalCanvasViewHandler>(ViewMapper){
        [nameof(MetalCanvasView.IsRunning)]=MapRunning,
        [nameof(MetalCanvasView.FramesPerSecond)]=MapFramesPerSecond,
    };

    public static readonly CommandMapper<MetalCanvasView,MetalCanvasViewHandler> Commands=new(ViewCommandMapper){
        [nameof(MetalCanvasView.RequestRedraw)]=MapRequestRedraw,
    };

    protected override SKMetalView CreatePlatformView(){
        return new SKMetalView{Paused=true,EnableSetNeedsDisplay=true};
    }

    protected override void ConnectHandler(SKMetalView platformView){
        base.ConnectHandler(platformView);
        platformView.PaintSurface+=this.OnPaintSurface;
    }

    protected override void DisconnectHandler(SKMetalView platformView){
        platformView.PaintSurface-=this.OnPaintSurface;
        platformView.Paused=true;
        base.DisconnectHandler(platformView);
    }

    private void OnPaintSurface(object? sender,SKPaintMetalSurfaceEventArgs e){
        this.VirtualView?.Renderer.Render(e.Surface.Canvas,new SKSizeI(e.BackendRenderTarget.Width,e.BackendRenderTarget.Height),e.Surface.Context);
    }

    private static void MapRunning(MetalCanvasViewHandler handler,MetalCanvasView view){
        handler.PlatformView.Paused=!view.IsRunning;
        handler.PlatformView.EnableSetNeedsDisplay=!view.IsRunning;
    }

    private static void MapFramesPerSecond(MetalCanvasViewHandler handler,MetalCanvasView view){
        handler.PlatformView.PreferredFramesPerSecond=view.FramesPerSecond;
    }

    private static void MapRequestRedraw(MetalCanvasViewHandler handler,MetalCanvasView view,object? args){
        handler.PlatformView.SetNeedsDisplay();
    }
}
#endif
