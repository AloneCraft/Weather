#if IOS
using Microsoft.Maui.Handlers;
using SkiaSharp;
using SkiaSharp.Views.iOS;

namespace Weather.App.Controls;

/// <summary>
/// iOS の Metal 描画ホスト(Rendering.md 決定事項)。MAUI 標準の SKGLView は iOS で OpenGL ES(非推奨)を使うため、
/// SkiaSharp.Views.iOS の SKMetalView(MTKView)を包む。連続描画は MTKView の PreferredFramesPerSecond で行う。
/// </summary>
public sealed class SceneMetalView(SceneHost host):View{
    public static readonly BindableProperty IsRunningProperty=BindableProperty.Create(nameof(IsRunning),typeof(bool),typeof(SceneMetalView),false);
    public static readonly BindableProperty FramesPerSecondProperty=BindableProperty.Create(nameof(FramesPerSecond),typeof(int),typeof(SceneMetalView),60);

    public SceneHost Host{get;}=host;

    public bool IsRunning{
        get=>(bool)this.GetValue(IsRunningProperty);
        set=>this.SetValue(IsRunningProperty,value);
    }

    public int FramesPerSecond{
        get=>(int)this.GetValue(FramesPerSecondProperty);
        set=>this.SetValue(FramesPerSecondProperty,value);
    }
}

public sealed class SceneMetalViewHandler():ViewHandler<SceneMetalView,SKMetalView>(Mapper){
    public static readonly IPropertyMapper<SceneMetalView,SceneMetalViewHandler> Mapper=new PropertyMapper<SceneMetalView,SceneMetalViewHandler>(ViewMapper){
        [nameof(SceneMetalView.IsRunning)]=MapRunning,
        [nameof(SceneMetalView.FramesPerSecond)]=MapFramesPerSecond,
    };

    protected override SKMetalView CreatePlatformView(){
        return new SKMetalView{Paused=true};
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
        this.VirtualView?.Host.Render(e.Surface.Canvas,new SKSizeI(e.BackendRenderTarget.Width,e.BackendRenderTarget.Height));
    }

    private static void MapRunning(SceneMetalViewHandler handler,SceneMetalView view){
        handler.PlatformView.Paused=!view.IsRunning;
    }

    private static void MapFramesPerSecond(SceneMetalViewHandler handler,SceneMetalView view){
        handler.PlatformView.PreferredFramesPerSecond=view.FramesPerSecond;
    }
}
#endif
