using System.Collections.Concurrent;
using SkiaSharp;
using Weather.Scene;

namespace Weather.Rendering.Scene;

/// <summary>SkSL の読み込みとコンパイル結果のキャッシュ。失敗は記録し、レイヤーは代替描画に切り替える。</summary>
public static class ShaderCache{
    private static readonly ConcurrentDictionary<string,SKRuntimeEffect?> Effects=new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string,string> ErrorsByKey=new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string,string> Errors=>ErrorsByKey;

    /// <summary>テスト用: 指定したシェーダーを壊れたものとして扱う。</summary>
    internal static Func<string,bool>? ForceFailure{get;set;}

    public static SKRuntimeEffect? Get(string name,int octaves=4){
        var key=name+"#"+octaves.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(ForceFailure?.Invoke(name)==true){
            return null;
        }
        return Effects.GetOrAdd(key,_=>Compile(name,key,octaves));
    }

    private static SKRuntimeEffect? Compile(string name,string key,int octaves){
        var source=Load(name).Replace("OCTAVES",octaves.ToString(System.Globalization.CultureInfo.InvariantCulture),StringComparison.Ordinal);
        var effect=SKRuntimeEffect.CreateShader(source,out var errors);
        if(effect is null){
            ErrorsByKey[key]=errors??"不明なエラー";
        }
        return effect;
    }

    private static string Load(string name){
        using var stream=typeof(ShaderCache).Assembly.GetManifestResourceStream("Weather.Rendering.Shaders."+name+".sksl")
            ??throw new InvalidOperationException($"シェーダーがありません: {name}");
        using var reader=new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// レイヤーごとの uniform。SKRuntimeShaderBuilder は破棄時に共有の SKRuntimeEffect まで破棄してしまう
/// (4.153.1 で確認)ため使わず、effect.ToShader(uniforms) でシェーダーを作る。
/// </summary>
internal sealed class ShaderUniforms:IDisposable{
    private SKRuntimeEffect? effect;
    private SKRuntimeEffectUniforms? uniforms;

    public SKRuntimeEffectUniforms For(SKRuntimeEffect target){
        if(!ReferenceEquals(this.effect,target)||this.uniforms is null){
            this.uniforms?.Dispose();
            this.uniforms=new SKRuntimeEffectUniforms(target);
            this.effect=target;
        }
        return this.uniforms;
    }

    public void Dispose(){
        this.uniforms?.Dispose();
    }
}

/// <summary>
/// 雲(SkSL の fBm)。品質段階に応じた解像度のオフスクリーンに描いて拡大する。
/// GPU の文脈があれば GPU のオフスクリーンを使う。シェーダーが使えなければ楕円の代替描画。
/// </summary>
internal sealed class CloudLayer(bool near):ISceneLayer{
    private readonly SKPaint paint=new(){IsAntialias=false};
    private readonly SKPaint blit=new();
    private SKSurface? surface;
    private SKSizeI surfaceSize;
    private GRRecordingContext? surfaceContext;
    private readonly ShaderUniforms uniforms=new();
    private double drift;

    public void Draw(SceneFrame frame){
        var s=frame.State;
        var cover=s.CloudCover;
        if(near){
            cover=Math.Clamp(cover*1.05-0.15,0,1);
        }
        if(cover<0.02){
            return;
        }
        var speed=0.0025;
        if(near){
            speed=0.0045;
        }
        if(frame.Options.ReduceMotion){
            speed*=0.15;
        }
        this.drift+=frame.WindDrift*speed*frame.DeltaSeconds+0.002*frame.DeltaSeconds;
        var effect=ShaderCache.Get("clouds",frame.Quality.Octaves);
        if(effect is null){
            this.DrawFallback(frame,cover);
            return;
        }
        var scale=frame.Quality.CloudScale;
        var size=new SKSizeI(Math.Max(1,(int)(frame.Width*scale)),Math.Max(1,(int)(frame.Height*scale)));
        var context=frame.Canvas.Context;
        if(this.surface is null||size!=this.surfaceSize||!ReferenceEquals(context,this.surfaceContext)){
            this.surface?.Dispose();
            var info=new SKImageInfo(size.Width,size.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
            this.surface=null;
            if(context is not null){
                this.surface=SKSurface.Create(context,false,info);
            }
            this.surface??=SKSurface.Create(info);
            this.surfaceSize=size;
            this.surfaceContext=context;
        }
        var (light,shadow)=Lighting(s,near,frame.Flash);
        var darkness=s.CloudDarkness;
        var patternScale=2.6f;
        var seed=1.3f;
        var top=0f;
        var bottom=0.7f;
        if(near){
            darkness+=0.15;
            patternScale=1.6f;
            seed=3.7f;
            top=0.02f;
            bottom=0.8f;
        }
        var u=this.uniforms.For(effect);
        u["uResolution"]=new[]{(float)size.Width,size.Height};
        u["uTime"]=1f;
        u["uCover"]=(float)cover;
        u["uDarkness"]=(float)Math.Clamp(darkness,0,1);
        u["uWind"]=new[]{(float)this.drift,0f};
        u["uLight"]=ColorMath.Rgb(light);
        u["uShadow"]=ColorMath.Rgb(shadow);
        u["uScale"]=patternScale;
        u["uSeed"]=seed;
        u["uTop"]=top;
        u["uBottom"]=bottom;
        using var shader=effect.ToShader(u);
        var canvas=this.surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        this.paint.Shader=shader;
        canvas.DrawRect(0,0,size.Width,size.Height,this.paint);
        this.paint.Shader=null;
        using var image=this.surface.Snapshot();
        frame.Canvas.DrawImage(image,new SKRect(0,0,frame.Width,frame.Height),new SKSamplingOptions(SKFilterMode.Linear),this.blit);
    }

    internal static (SKColor Light,SKColor Shadow) Lighting(DisplayState s,bool near,double flash){
        var day=s.Daylight;
        var light=ColorMath.Lerp(new SKColor(0x3c,0x44,0x5c),new SKColor(0xff,0xff,0xff),day);
        light=ColorMath.Lerp(light,new SKColor(0xff,0xc4,0x96),s.Golden*0.6);
        var shadow=ColorMath.Lerp(new SKColor(0x14,0x18,0x24),new SKColor(0x9a,0xa4,0xb4),day);
        if(near&&s.CloudType==SceneCloudType.Cumulonimbus){
            shadow=ColorMath.Darken(shadow,0.35);
        }
        if(flash>0){
            light=ColorMath.Lerp(light,new SKColor(0xe8,0xec,0xff),flash);
            shadow=ColorMath.Lerp(shadow,new SKColor(0xa8,0xb0,0xd8),flash*0.8);
        }
        return (light,shadow);
    }

    private void DrawFallback(SceneFrame frame,double cover){
        var (light,_)=Lighting(frame.State,near,frame.Flash);
        this.paint.Color=light.WithAlpha((byte)(200*cover));
        using var blur=SKMaskFilter.CreateBlur(SKBlurStyle.Normal,frame.Height*0.03f);
        this.paint.MaskFilter=blur;
        var count=(int)(4+cover*10);
        for(var i=0;i<count;i++){
            var x=(float)((DeterministicNoise.Hash(i,7)+this.drift*0.1)%1.2-0.1)*frame.Width;
            var y=(float)(DeterministicNoise.Hash(i,8)*0.55)*frame.HorizonY;
            frame.Canvas.DrawOval(x,y,frame.Width*0.18f,frame.Height*0.05f,this.paint);
        }
        this.paint.MaskFilter=null;
    }

    public void Dispose(){
        this.paint.Dispose();
        this.blit.Dispose();
        this.surface?.Dispose();
        this.uniforms.Dispose();
    }
}

/// <summary>霧(SkSL)。シェーダーが使えなければ縦グラデーションで代替する。</summary>
internal sealed class FogLayer:ISceneLayer{
    private readonly SKPaint paint=new();
    private readonly ShaderUniforms uniforms=new();

    public void Draw(SceneFrame frame){
        var s=frame.State;
        if(s.Fog<0.02){
            return;
        }
        var color=ColorMath.Lerp(new SKColor(0x30,0x34,0x3e),new SKColor(0xd8,0xdc,0xe2),s.Daylight);
        var effect=ShaderCache.Get("fog");
        if(effect is null){
            using var gradient=SKShader.CreateLinearGradient(new SKPoint(0,0),new SKPoint(0,frame.Height),[color.WithAlpha((byte)(90*s.Fog)),color.WithAlpha((byte)(230*s.Fog))],SKShaderTileMode.Clamp);
            this.paint.Shader=gradient;
            frame.Canvas.DrawRect(0,0,frame.Width,frame.Height,this.paint);
            this.paint.Shader=null;
            return;
        }
        var u=this.uniforms.For(effect);
        u["uResolution"]=new[]{frame.Width,frame.Height};
        var time=(float)frame.Time;
        if(frame.Options.ReduceMotion){
            time*=0.15f;
        }
        u["uTime"]=time;
        u["uDensity"]=(float)s.Fog;
        u["uColor"]=ColorMath.Rgb(color);
        using var shader=effect.ToShader(u);
        this.paint.Shader=shader;
        frame.Canvas.DrawRect(0,0,frame.Width,frame.Height,this.paint);
        this.paint.Shader=null;
    }

    public void Dispose(){
        this.paint.Dispose();
        this.uniforms.Dispose();
    }
}
