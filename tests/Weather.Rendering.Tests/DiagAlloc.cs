using SkiaSharp;
using Weather.Rendering.Scene;
using Weather.Scene;

namespace Weather.Rendering.Tests;

//一時的な診断(計測のあと revert する)
public class DiagAlloc{
    [Fact,Trait("Category","Unit")]public void Diag_RendererAlloc(){
        using var renderer=new Rendering.Scene.SceneRenderer(new SceneRenderOptions{AutoTier=false,Tier=QualityTier.Low});
        renderer.SetTarget(Scenes.Make(30,180,0.9,0.5,SceneCloudType.Stratus,ScenePrecipitationType.Rain,0.6));
        using var bitmap=new SKBitmap(120,200);
        using var canvas=new SKCanvas(bitmap);
        for(var i=0;i<60;i++){
            renderer.Render(canvas,new SKSizeI(120,200),i/30d);
        }
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=60;i<120;i++){
            renderer.Render(canvas,new SKSizeI(120,200),i/30d);
        }
        var perFrame=(GC.GetAllocatedBytesForCurrentThread()-before)/60;
        Assert.Fail($"DIAG RENDERER {perFrame} B/frame");
    }

    [Fact,Trait("Category","Unit")]public void Diag_LayerAlloc(){
        var state=Scenes.Make(30,180,0.9,0.5,SceneCloudType.Stratus,ScenePrecipitationType.Rain,0.6);
        var display=new DisplayState();
        display.Snap(state);
        var options=new SceneRenderOptions{AutoTier=false,Tier=QualityTier.Low};
        using var bitmap=new SKBitmap(120,200);
        using var canvas=new SKCanvas(bitmap);
        var sky=new SkyLayer();
        var stars=new StarsLayer();
        var celestial=new CelestialLayer();
        var far=new CloudLayer(false);
        var landscape=new LandscapeLayer();
        var near=new CloudLayer(true);
        var precipitation=new PrecipitationLayer();
        var fog=new FogLayer();
        var haze=new HazeLayer();
        var lightning=new LightningLayer();
        var frame=new SceneFrame();
        frame.Canvas=canvas;
        frame.Size=new SKSizeI(120,200);
        frame.State=display;
        frame.Quality=QualitySettings.For(options.Tier);
        frame.Options=options;
        var totals=new Dictionary<string,long>();
        void Measure(string name,Action action){
            var before=GC.GetAllocatedBytesForCurrentThread();
            action();
            var after=GC.GetAllocatedBytesForCurrentThread();
            totals[name]=totals.GetValueOrDefault(name)+(after-before);
        }
        for(var i=0;i<140;i++){
            frame.Time=i/30d;
            frame.DeltaSeconds=1/30d;
            if(i>=80){
                Measure("lightning.Update",()=>frame.Flash=lightning.Update(frame));
                Measure("sky",()=>sky.Draw(frame));
                Measure("stars",()=>stars.Draw(frame));
                Measure("celestial",()=>celestial.Draw(frame));
                Measure("farClouds",()=>far.Draw(frame));
                Measure("landscape",()=>landscape.Draw(frame));
                Measure("nearClouds",()=>near.Draw(frame));
                Measure("precipitation",()=>precipitation.Draw(frame));
                Measure("fog",()=>fog.Draw(frame));
                Measure("haze",()=>haze.Draw(frame));
                Measure("lightning.Draw",()=>lightning.Draw(frame));
            }else{
                frame.Flash=lightning.Update(frame);
                sky.Draw(frame);
                stars.Draw(frame);
                celestial.Draw(frame);
                far.Draw(frame);
                landscape.Draw(frame);
                near.Draw(frame);
                precipitation.Draw(frame);
                fog.Draw(frame);
                haze.Draw(frame);
                lightning.Draw(frame);
            }
        }
        var lines=totals.OrderByDescending(static p=>p.Value).Select(static p=>$"{p.Key}={p.Value/60}B");
        Assert.Fail("DIAG LAYERS "+string.Join(", ",lines));
    }
}
