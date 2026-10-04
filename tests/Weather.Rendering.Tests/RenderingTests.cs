using SkiaSharp;
using Weather.Core;
using Weather.Geo;
using Weather.Rendering;
using Weather.Rendering.Charts;
using Weather.Rendering.Map;
using Weather.Rendering.Scene;
using Weather.Scene;

namespace Weather.Rendering.Tests;

internal static class Scenes{
    private static readonly DateTimeOffset Time=new(2026,10,4,3,0,0,TimeSpan.Zero);

    public static SceneState Make(double sunAltitude,double sunAzimuth,double cover,double darkness,SceneCloudType cloudType,ScenePrecipitationType precipitation=ScenePrecipitationType.None,double intensity=0,double thunder=0,double fog=0,double moonAltitude=-30){
        return new SceneState{
            Time=Time,
            Sun=new CelestialPosition(sunAltitude,sunAzimuth),
            Moon=new MoonState(new CelestialPosition(moonAltitude,140),0.4,0.8),
            Daylight=Weather.Scene.Astronomy.SolarPosition.Phase(sunAltitude),
            Clouds=new CloudState(cover,darkness,cloudType),
            Precipitation=new PrecipitationState(precipitation,intensity,0),
            Wind=new WindState(6,270,0.2),
            ThunderActivity=thunder,
            FogDensity=fog,
            Haze=new HazeState(HazeKind.None,0),
            Origin=new SceneOrigin(SceneOriginKind.TimeSeries,ProviderId.MetNorway,Time,Time.AddHours(1)),
        };
    }

    public static IEnumerable<(string Name,SceneState State)> Canonical(){
        yield return ("clear-day",Make(45,180,0.05,0.1,SceneCloudType.Cumulus));
        yield return ("sunset",Make(1,255,0.35,0.2,SceneCloudType.Cumulus));
        yield return ("overcast",Make(30,180,0.95,0.45,SceneCloudType.Stratus));
        yield return ("thunder-night",Make(-30,0,0.95,0.9,SceneCloudType.Cumulonimbus,ScenePrecipitationType.Rain,0.9,0.9,moonAltitude:30));
        yield return ("heavy-snow",Make(20,180,0.95,0.5,SceneCloudType.Stratus,ScenePrecipitationType.Snow,0.9));
        yield return ("dense-fog",Make(15,150,0.8,0.3,SceneCloudType.Stratus,fog:0.85));
    }

    /// <summary>決定的に描画する(固定の時刻列で 40 フレーム進めた最後の画像)。</summary>
    public static SKBitmap Render(SceneState state,QualityTier tier,int width=360,int height=640,int frames=40){
        using var renderer=new Rendering.Scene.SceneRenderer(new SceneRenderOptions{Tier=tier,AutoTier=false});
        renderer.SetTarget(state);
        var bitmap=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Premul);
        using var canvas=new SKCanvas(bitmap);
        for(var i=0;i<frames;i++){
            renderer.Render(canvas,new SKSizeI(width,height),i/30d);
        }
        return bitmap;
    }
}

[Collection("ShaderCache")]
public class SceneRenderer{
    [Fact,Trait("Category","Unit")]public void Render(){
        {
            //全シーン × 全品質段階で例外なく描画でき、単色にならない
            foreach(var (name,state) in Scenes.Canonical()){
                foreach(var tier in Enum.GetValues<QualityTier>()){
                    using var bitmap=Scenes.Render(state,tier,180,320,6);
                    Assert.True(DistinctColors(bitmap)>8,$"{name}/{tier}");
                }
            }
        }
        {
            //雨のシーンでは粒子が出る。晴れでは出ない
            using var rain=new Rendering.Scene.SceneRenderer(new SceneRenderOptions{AutoTier=false});
            rain.SetTarget(Scenes.Make(30,180,0.9,0.5,SceneCloudType.Stratus,ScenePrecipitationType.Rain,0.6));
            using var bitmap=new SKBitmap(200,300);
            using var canvas=new SKCanvas(bitmap);
            for(var i=0;i<30;i++){
                rain.Render(canvas,new SKSizeI(200,300),i/30d);
            }
            Assert.True(rain.ParticleCount>100);
            using var clear=new Rendering.Scene.SceneRenderer(new SceneRenderOptions{AutoTier=false});
            clear.SetTarget(Scenes.Make(30,180,0.05,0.1,SceneCloudType.Cumulus));
            for(var i=0;i<30;i++){
                clear.Render(canvas,new SKSizeI(200,300),i/30d);
            }
            Assert.Equal(0,clear.ParticleCount);
        }
    }

    [Fact,Trait("Category","Unit")]public void Render_ShaderFailure(){
        //シェーダーが壊れていても代替描画で表示を続ける
        ShaderCache.ForceFailure=static _=>true;
        try{
            using var bitmap=Scenes.Render(Scenes.Make(20,180,0.8,0.3,SceneCloudType.Stratus,fog:0.6),QualityTier.Medium,180,320,5);
            Assert.True(DistinctColors(bitmap)>8);
        }finally{
            ShaderCache.ForceFailure=null;
        }
    }

    [Fact,Trait("Category","Unit")]public void Render_Lightning(){
        //稲光の閃光は 1 秒に 3 回以下(WCAG 2.3.1)
        using var renderer=new Rendering.Scene.SceneRenderer(new SceneRenderOptions{AutoTier=false,Tier=QualityTier.Low});
        renderer.SetTarget(Scenes.Make(-30,0,0.95,0.9,SceneCloudType.Cumulonimbus,ScenePrecipitationType.Rain,0.9,1));
        using var bitmap=new SKBitmap(90,160);
        using var canvas=new SKCanvas(bitmap);
        var perSecond=new int[61];
        var last=0;
        for(var i=0;i<60*30;i++){
            var t=i/30d;
            renderer.Render(canvas,new SKSizeI(90,160),t);
            var count=renderer.LightningCount;
            perSecond[(int)t]+=count-last;
            last=count;
        }
        Assert.True(renderer.LightningCount>5);
        Assert.All(perSecond,static n=>Assert.True(n<=LightningLayer.MaxFlashesPerSecond));
    }

    [Fact,Trait("Category","Unit")]public void Render_Allocation(){
        //定常状態の 1 フレームあたりのマネージド割り当てが小さい(粒子のバッファは使い回す)
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
        Assert.True(perFrame<4096,$"1 フレームあたり {perFrame} バイト");
    }

    [Fact,Trait("Category","Golden")]public void Render_Golden(){
        //代表 6 シーンのゴールデン画像と許容誤差付きで比較する(更新: WEATHER_UPDATE_GOLDEN=1)
        var update=Environment.GetEnvironmentVariable("WEATHER_UPDATE_GOLDEN")=="1";
        var sourceDir=Path.Combine(FindTestsRoot(),"Golden");
        foreach(var (name,state) in Scenes.Canonical()){
            using var bitmap=Scenes.Render(state,QualityTier.Medium);
            var file=Path.Combine(sourceDir,name+".png");
            if(update){
                using var image=SKImage.FromBitmap(bitmap);
                using var data=image.Encode(SKEncodedImageFormat.Png,100);
                File.WriteAllBytes(file,data.ToArray());
                continue;
            }
            Assert.True(File.Exists(file),$"ゴールデン画像がありません: {name}(WEATHER_UPDATE_GOLDEN=1 で生成)");
            using var golden=SKBitmap.Decode(file);
            var diff=MeanDifference(bitmap,golden);
            Assert.True(diff<3,$"{name}: 平均差 {diff:0.00}");
        }
    }

    private static string FindTestsRoot(){
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"Weather.Rendering.Tests.csproj"))){
            dir=dir.Parent;
        }
        return dir?.FullName??AppContext.BaseDirectory;
    }

    private static int DistinctColors(SKBitmap bitmap){
        var set=new HashSet<uint>();
        for(var y=0;y<bitmap.Height;y+=4){
            for(var x=0;x<bitmap.Width;x+=4){
                set.Add((uint)bitmap.GetPixel(x,y));
            }
        }
        return set.Count;
    }

    private static double MeanDifference(SKBitmap a,SKBitmap b){
        Assert.Equal(a.Width,b.Width);
        Assert.Equal(a.Height,b.Height);
        double sum=0;
        var n=0;
        for(var y=0;y<a.Height;y+=2){
            for(var x=0;x<a.Width;x+=2){
                var ca=a.GetPixel(x,y);
                var cb=b.GetPixel(x,y);
                sum+=Math.Abs(ca.Red-cb.Red)+Math.Abs(ca.Green-cb.Green)+Math.Abs(ca.Blue-cb.Blue);
                n+=3;
            }
        }
        return sum/n;
    }
}

public class TimeSeriesChart{
    private static double Rain(int hour){
        if(hour%7==0){
            return 2.5;
        }
        return 0;
    }

    [Fact,Trait("Category","Unit")]public void Draw(){
        var start=new DateTimeOffset(2026,10,4,0,0,0,TimeSpan.Zero);
        var samples=Enumerable.Range(0,48).Select(i=>new ChartSample(start.AddHours(i),15+5*Math.Sin(i/4.0),Rain(i),i*2%100)).ToList();
        using var chart=new Charts.TimeSeriesChart();
        using var bitmap=new SKBitmap(400,200);
        using var canvas=new SKCanvas(bitmap);
        {
            //例外なく描画できる(書体は端末の CJK 書体)
            chart.Draw(canvas,new SKRect(0,0,400,200),samples,new ChartStyle{Zone=TimeZoneInfo.Utc},start.AddHours(10));
        }
        {
            //x 座標 → 時刻
            Assert.Equal(start.AddHours(23.5),Charts.TimeSeriesChart.HitTest(new SKRect(0,0,400,200),samples,200));
            Assert.Equal(start,Charts.TimeSeriesChart.HitTest(new SKRect(0,0,400,200),samples,-50));
        }
    }
}

public class ArchitectureRules{
    [Fact,Trait("Category","Unit")]public void NoMauiReference(){
        var names=typeof(Rendering.Scene.SceneRenderer).Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"");
        Assert.DoesNotContain(names,static n=>n.StartsWith("Microsoft.Maui",StringComparison.Ordinal));
    }
}
