using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Weather.Core;
using Weather.Rendering;
using Weather.Rendering.Scene;
using Weather.Scene;

namespace Weather.SceneLab;

/// <summary>SceneLab のメイン画面(左: 描画、右: 操作パネル)。</summary>
internal sealed class MainWindow:Window{
    //SkiaSharp.Views.WPF 4.x は .NET Framework 向けのみのため、WriteableBitmap に直接描く
    private readonly System.Windows.Controls.Image canvas=new(){Stretch=Stretch.Fill};
    private WriteableBitmap? bitmap;
    private readonly SceneRenderer renderer=new(new SceneRenderOptions{AutoTier=false});
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly Dictionary<string,Slider> sliders=new(StringComparer.Ordinal);
    private readonly ComboBox precipitation=new();
    private readonly ComboBox tier=new();
    private readonly ComboBox fixture=new();
    private readonly Slider timeline=new(){Minimum=0,Maximum=1,IsEnabled=false};
    private readonly TextBlock status=new(){Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox reduceMotion=new(){Content="視差効果を減らす",Foreground=Brushes.White};
    private readonly CheckBox reduceFlashes=new(){Content="稲光の点滅を抑える",Foreground=Brushes.White};
    private SceneTimeline? sceneTimeline;
    private Forecast? forecast;
    private bool updating;

    public MainWindow(){
        this.Title="SceneLab — シーン描画の調整";
        this.Width=1100;
        this.Height=780;
        this.Background=new SolidColorBrush(Color.FromRgb(0x14,0x18,0x24));
        var root=new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(340)});
        var host=new Border{Child=this.canvas};
        host.SizeChanged+=(_,_)=>this.bitmap=null;
        root.Children.Add(host);
        var panel=this.BuildPanel();
        Grid.SetColumn(panel,1);
        root.Children.Add(panel);
        this.Content=root;
        this.ApplySliders();
        this.renderer.SnapToTarget();
        CompositionTarget.Rendering+=(_,_)=>this.RenderFrame(host);
    }

    private ScrollViewer BuildPanel(){
        var stack=new StackPanel{Margin=new Thickness(12)};
        stack.Children.Add(Header("記録データの予報を再生"));
        foreach(var (name,_) in FixtureForecasts.Locations){
            this.fixture.Items.Add(name);
        }
        var load=new Button{Content="読み込む",Margin=new Thickness(0,4,0,4)};
        load.Click+=async (_,_)=>await this.LoadFixtureAsync();
        stack.Children.Add(this.fixture);
        stack.Children.Add(load);
        this.timeline.ValueChanged+=(_,_)=>this.OnTimeline();
        stack.Children.Add(this.timeline);
        stack.Children.Add(this.status);

        stack.Children.Add(Header("シーン状態(スライダーで直接操作)"));
        this.AddSlider(stack,"sunAltitude","太陽高度(°)",-30,80,35);
        this.AddSlider(stack,"sunAzimuth","太陽方位(°)",60,300,180);
        this.AddSlider(stack,"moonAltitude","月高度(°)",-30,80,-20);
        this.AddSlider(stack,"moonPhase","月の位相",0,1,0.4);
        this.AddSlider(stack,"cloudCover","雲量",0,1,0.4);
        this.AddSlider(stack,"cloudDarkness","雲の暗さ",0,1,0.3);
        foreach(var type in Enum.GetValues<ScenePrecipitationType>()){
            this.precipitation.Items.Add(type);
        }
        this.precipitation.SelectedIndex=0;
        this.precipitation.SelectionChanged+=(_,_)=>this.ApplySliders();
        stack.Children.Add(Label("降水種別"));
        stack.Children.Add(this.precipitation);
        this.AddSlider(stack,"intensity","降水強度",0,1,0);
        this.AddSlider(stack,"intermittency","断続性",0,1,0);
        this.AddSlider(stack,"windSpeed","風速(m/s)",0,25,4);
        this.AddSlider(stack,"windDirection","風向(°)",0,359,270);
        this.AddSlider(stack,"thunder","雷",0,1,0);
        this.AddSlider(stack,"fog","霧",0,1,0);
        this.AddSlider(stack,"haze","もや",0,1,0);

        stack.Children.Add(Header("描画"));
        foreach(var t in Enum.GetValues<QualityTier>()){
            this.tier.Items.Add(t);
        }
        this.tier.SelectedItem=QualityTier.Medium;
        this.tier.SelectionChanged+=(_,_)=>this.renderer.Options.Tier=(QualityTier)this.tier.SelectedItem;
        stack.Children.Add(this.tier);
        this.reduceMotion.Checked+=(_,_)=>this.renderer.Options.ReduceMotion=true;
        this.reduceMotion.Unchecked+=(_,_)=>this.renderer.Options.ReduceMotion=false;
        this.reduceFlashes.Checked+=(_,_)=>this.renderer.Options.ReduceFlashes=true;
        this.reduceFlashes.Unchecked+=(_,_)=>this.renderer.Options.ReduceFlashes=false;
        stack.Children.Add(this.reduceMotion);
        stack.Children.Add(this.reduceFlashes);
        var export=new Button{Content="PNG に書き出す",Margin=new Thickness(0,8,0,0)};
        export.Click+=(_,_)=>this.Export();
        stack.Children.Add(export);
        return new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private void AddSlider(StackPanel stack,string key,string label,double min,double max,double value){
        var text=Label(label);
        var slider=new Slider{Minimum=min,Maximum=max,Value=value};
        slider.ValueChanged+=(_,_)=>{
            text.Text=label+": "+slider.Value.ToString("0.00",CultureInfo.InvariantCulture);
            this.ApplySliders();
        };
        text.Text=label+": "+value.ToString("0.00",CultureInfo.InvariantCulture);
        this.sliders[key]=slider;
        stack.Children.Add(text);
        stack.Children.Add(slider);
    }

    private double Value(string key){
        if(this.sliders.TryGetValue(key,out var slider)){
            return slider.Value;
        }
        return 0;
    }

    /// <summary>スライダーの値から SceneState を作って目標にする。</summary>
    private void ApplySliders(){
        if(this.updating||this.sliders.Count<14){
            return;
        }
        var type=ScenePrecipitationType.None;
        if(this.precipitation.SelectedItem is ScenePrecipitationType selected){
            type=selected;
        }
        var intensity=this.Value("intensity");
        if(type==ScenePrecipitationType.None){
            intensity=0;
        }else if(intensity<=0){
            intensity=0.01;
        }
        var thunder=this.Value("thunder");
        if(type==ScenePrecipitationType.None){
            thunder=0;
        }
        var time=DateTimeOffset.UnixEpoch;
        var state=new SceneState{
            Time=time,
            Sun=new CelestialPosition(this.Value("sunAltitude"),this.Value("sunAzimuth")),
            Moon=new MoonState(new CelestialPosition(this.Value("moonAltitude"),150),this.Value("moonPhase"),0.5-0.5*Math.Cos(this.Value("moonPhase")*Math.PI*2)),
            Daylight=Weather.Scene.Astronomy.SolarPosition.Phase(this.Value("sunAltitude")),
            Clouds=new CloudState(this.Value("cloudCover"),this.Value("cloudDarkness"),CloudType(thunder,type)),
            Precipitation=new PrecipitationState(type,intensity,this.Value("intermittency")),
            Wind=new WindState(this.Value("windSpeed"),this.Value("windDirection"),0.2),
            ThunderActivity=thunder,
            FogDensity=this.Value("fog"),
            Haze=new HazeState(HazeKindFor(this.Value("haze")),this.Value("haze")),
            Origin=new SceneOrigin(SceneOriginKind.None,null,null,null),
        };
        this.renderer.SetTarget(state);
    }

    private static SceneCloudType CloudType(double thunder,ScenePrecipitationType type){
        if(thunder>0){
            return SceneCloudType.Cumulonimbus;
        }
        if(type!=ScenePrecipitationType.None){
            return SceneCloudType.Stratus;
        }
        return SceneCloudType.Cumulus;
    }

    private static HazeKind HazeKindFor(double density){
        if(density>0){
            return HazeKind.Haze;
        }
        return HazeKind.None;
    }

    private async Task LoadFixtureAsync(){
        if(this.fixture.SelectedIndex<0){
            this.fixture.SelectedIndex=0;
        }
        try{
            this.forecast=await FixtureForecasts.LoadAsync(this.fixture.SelectedIndex);
            this.sceneTimeline=new SceneTimeline(this.forecast,new SceneConverter());
            this.timeline.IsEnabled=this.sceneTimeline.Start is not null;
            this.timeline.Value=0;
            this.status.Text=$"時系列 {this.forecast.TimeSeries.Count} 区間 / 日別 {this.forecast.Daily.Count} 日";
            this.OnTimeline();
        }catch(WeatherProviderException ex){
            this.status.Text="読み込みに失敗しました: "+ex.Message;
        }
    }

    /// <summary>時間軸の位置 → 変換器の SceneState(SceneTimeline の補間)。スライダーにも反映する。</summary>
    private void OnTimeline(){
        if(this.sceneTimeline?.Start is not {} start||this.sceneTimeline.End is not {} end||this.forecast is null){
            return;
        }
        var t=start+TimeSpan.FromSeconds((end-start).TotalSeconds*this.timeline.Value);
        var state=this.sceneTimeline.At(t);
        this.renderer.SetTarget(state);
        var zone=FindZone(this.forecast.Location.TimeZoneId);
        this.status.Text=TimeZoneInfo.ConvertTime(t,zone).ToString("M/d HH:mm",CultureInfo.InvariantCulture)
            +$"  雲 {state.Clouds.Cover:0.00} 降水 {state.Precipitation.Type} {state.Precipitation.Intensity:0.00} 霧 {state.FogDensity:0.00} 雷 {state.ThunderActivity:0.00}";
        this.updating=true;
        this.sliders["sunAltitude"].Value=Math.Clamp(state.Sun.AltitudeDeg,-30,80);
        this.sliders["sunAzimuth"].Value=Math.Clamp(state.Sun.AzimuthDeg,60,300);
        this.sliders["cloudCover"].Value=state.Clouds.Cover;
        this.sliders["intensity"].Value=state.Precipitation.Intensity;
        this.sliders["fog"].Value=state.FogDensity;
        this.updating=false;
    }

    private static TimeZoneInfo FindZone(string id){
        try{
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }catch(TimeZoneNotFoundException){
            return TimeZoneInfo.Utc;
        }
    }

    private void RenderFrame(FrameworkElement host){
        var width=(int)Math.Max(1,host.ActualWidth);
        var height=(int)Math.Max(1,host.ActualHeight);
        if(this.bitmap is null||this.bitmap.PixelWidth!=width||this.bitmap.PixelHeight!=height){
            this.bitmap=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
            this.canvas.Source=this.bitmap;
        }
        this.bitmap.Lock();
        try{
            var info=new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Premul);
            using var surface=SKSurface.Create(info,this.bitmap.BackBuffer,this.bitmap.BackBufferStride);
            this.renderer.Render(surface.Canvas,new SKSizeI(width,height),this.clock.Elapsed.TotalSeconds);
            this.bitmap.AddDirtyRect(new Int32Rect(0,0,width,height));
        }finally{
            this.bitmap.Unlock();
        }
    }

    private void Export(){
        var size=new SKSizeI(720,1280);
        using var bitmap=new SKBitmap(size.Width,size.Height);
        using var canvas=new SKCanvas(bitmap);
        this.renderer.Render(canvas,size,this.clock.Elapsed.TotalSeconds);
        using var image=SKImage.FromBitmap(bitmap);
        using var data=image.Encode(SKEncodedImageFormat.Png,100);
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),$"scenelab-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        File.WriteAllBytes(path,data.ToArray());
        this.status.Text="書き出しました: "+path;
    }

    private static TextBlock Header(string text){
        return new TextBlock{Text=text,Foreground=Brushes.White,FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,4)};
    }

    private static TextBlock Label(string text){
        return new TextBlock{Text=text,Foreground=Brushes.LightGray,Margin=new Thickness(0,6,0,0)};
    }
}
