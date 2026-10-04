using SkiaSharp;
using Weather.Core;
using Weather.Rendering.Scene;

namespace Weather.Rendering.Map;

/// <summary>SKRuntimeEffectChildren を使い回す(ShaderUniforms と同じく、共有のエフェクトを破棄しないため)。</summary>
internal sealed class ShaderChildren:IDisposable{
    private SKRuntimeEffect? effect;
    private SKRuntimeEffectChildren? children;

    public SKRuntimeEffectChildren For(SKRuntimeEffect target){
        if(!ReferenceEquals(this.effect,target)||this.children is null){
            this.children?.Dispose();
            this.children=new SKRuntimeEffectChildren(target);
            this.effect=target;
        }
        return this.children;
    }

    public void Dispose(){
        this.children?.Dispose();
    }
}

/// <summary>
/// 格子レイヤー(GFS の風速・降水・気温・雲量)。SkSL で画面の画素ごとに緯度経度を求め、格子を線形補間してパレットで着色する。
/// 日本周辺はシェーダーでも透明にする(格子の NaN と二重に守る。方針 5)。
/// </summary>
internal sealed class ScalarFieldLayer(JapanMaskTexture mask):IDisposable{
    private readonly SKPaint paint=new(){IsAntialias=false};
    private readonly ShaderUniforms uniforms=new();
    private readonly ShaderChildren children=new();
    private GridField? field;
    private Legend? legend;
    private SKImage? texture;
    private SKShader? fieldShader;
    private SKShader? paletteShader;
    private SKShader? maskShader;
    private SKShader? shader;
    private (MapView View,SKSize Size,float Opacity) key;

    public bool HasField=>this.field is not null;

    public void Set(GridField? value,Legend? valueLegend){
        if(ReferenceEquals(value,this.field)&&ReferenceEquals(valueLegend,this.legend)){
            return;
        }
        this.ReleaseField();
        this.field=value;
        this.legend=valueLegend;
        if(value is null||valueLegend is null){
            return;
        }
        var encoding=FieldEncoding.For(value.Quantity);
        this.texture=FieldTextures.Create(value,encoding);
        var xMode=SKShaderTileMode.Decal;
        if(value.Geometry.WrapsLongitude){
            xMode=SKShaderTileMode.Repeat;
        }
        this.fieldShader=this.texture.ToShader(xMode,SKShaderTileMode.Clamp,new SKSamplingOptions(SKFilterMode.Linear));
        this.paletteShader=MapPalettes.Get(valueLegend,encoding).ToShader(SKShaderTileMode.Clamp,SKShaderTileMode.Clamp,new SKSamplingOptions(SKFilterMode.Nearest));
        this.maskShader=mask.Image.ToShader(SKShaderTileMode.Decal,SKShaderTileMode.Decal,new SKSamplingOptions(SKFilterMode.Linear));
    }

    public void Draw(SKCanvas canvas,MapView view,SKSize size,float opacity){
        if(this.field is not {} f||this.fieldShader is null){
            return;
        }
        var effect=ShaderCache.Get("field");
        if(effect is null){
            return;
        }
        if(this.shader is null||this.key!=(view,size,opacity)){
            var u=this.uniforms.For(effect);
            var (ox,oy)=view.Origin(size);
            u["origin"]=new[]{(float)ox,(float)oy};
            u["worldSize"]=(float)view.WorldSize;
            u["gridOrigin"]=new[]{(float)f.Geometry.WestLongitude,(float)f.Geometry.NorthLatitude};
            u["gridStep"]=new[]{(float)f.Geometry.LongitudeStep,(float)f.Geometry.LatitudeStep};
            u["maskRect"]=mask.Rect;
            u["maskSize"]=mask.Size;
            u["opacity"]=opacity;
            var c=this.children.For(effect);
            c["field"]=this.fieldShader;
            c["palette"]=this.paletteShader;
            c["mask"]=this.maskShader;
            this.shader?.Dispose();
            this.shader=effect.ToShader(u,c);
            this.key=(view,size,opacity);
        }
        this.paint.Shader=this.shader;
        canvas.DrawRect(0,0,size.Width,size.Height,this.paint);
    }

    private void ReleaseField(){
        this.paint.Shader=null;
        this.shader?.Dispose();
        this.shader=null;
        this.fieldShader?.Dispose();
        this.fieldShader=null;
        this.paletteShader?.Dispose();
        this.paletteShader=null;
        this.maskShader?.Dispose();
        this.maskShader=null;
        this.texture?.Dispose();
        this.texture=null;
        this.field=null;
        this.legend=null;
    }

    public void Dispose(){
        this.ReleaseField();
        this.uniforms.Dispose();
        this.children.Dispose();
        this.paint.Dispose();
    }
}

/// <summary>
/// 日本周辺のマスクの描画。気象庁のタイルを日本周辺に限る(DstIn)、気象庁の予報期間外の斜線を描く。
/// </summary>
internal sealed class JapanOverlay(JapanMaskTexture mask):IDisposable{
    private readonly SKPaint clip=new(){BlendMode=SKBlendMode.DstIn};
    private readonly SKPaint hatch=new();
    private readonly ShaderUniforms uniforms=new();
    private readonly ShaderChildren children=new();
    private readonly SKShader maskShader=mask.Image.ToShader(SKShaderTileMode.Decal,SKShaderTileMode.Decal,new SKSamplingOptions(SKFilterMode.Linear));

    /// <summary>直前に描いた内容(SaveLayer の中)を日本周辺だけに残す。</summary>
    public void ClipToJapan(SKCanvas canvas,MapView view,SKSize size){
        using var shader=this.Create(view,size,0);
        if(shader is null){
            return;
        }
        this.clip.Shader=shader;
        canvas.DrawRect(0,0,size.Width,size.Height,this.clip);
        this.clip.Shader=null;
    }

    public void DrawOutOfRange(SKCanvas canvas,MapView view,SKSize size){
        using var shader=this.Create(view,size,1);
        if(shader is null){
            return;
        }
        this.hatch.Shader=shader;
        canvas.DrawRect(0,0,size.Width,size.Height,this.hatch);
        this.hatch.Shader=null;
    }

    private SKShader? Create(MapView view,SKSize size,float mode){
        var effect=ShaderCache.Get("japanmask");
        if(effect is null){
            return null;
        }
        var u=this.uniforms.For(effect);
        var (ox,oy)=view.Origin(size);
        u["origin"]=new[]{(float)ox,(float)oy};
        u["worldSize"]=(float)view.WorldSize;
        u["maskRect"]=mask.Rect;
        u["maskSize"]=mask.Size;
        u["mode"]=mode;
        u["density"]=view.PixelRatio;
        var c=this.children.For(effect);
        c["mask"]=this.maskShader;
        return effect.ToShader(u,c);
    }

    public void Dispose(){
        this.clip.Dispose();
        this.hatch.Dispose();
        this.uniforms.Dispose();
        this.children.Dispose();
        this.maskShader.Dispose();
    }
}

/// <summary>
/// 等圧線(海面気圧。4 hPa ごと、20 hPa ごとに太く)。格子のマーチングスクエアで、格子が変わったときだけ線分を作る。
/// NaN(日本周辺)のセルは線を引かない。
/// </summary>
internal sealed class IsobarLayer:IDisposable{
    public const double Interval=4;
    private readonly SKPaint thin=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.White.WithAlpha(120),StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint thick=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.White.WithAlpha(190),StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint halo=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=new SKColor(0x10,0x1a,0x2b,150),StrokeCap=SKStrokeCap.Round};
    private GridField? field;
    private SKPath? thinPath;
    private SKPath? thickPath;

    public int SegmentCount{get;private set;}

    public void Set(GridField? pressure){
        if(ReferenceEquals(pressure,this.field)){
            return;
        }
        this.field=pressure;
        this.thinPath?.Dispose();
        this.thickPath?.Dispose();
        this.thinPath=null;
        this.thickPath=null;
        this.SegmentCount=0;
        if(pressure is null){
            return;
        }
        var (thinSegments,thickSegments)=Contour(pressure);
        this.thinPath=ToPath(thinSegments);
        this.thickPath=ToPath(thickSegments);
        this.SegmentCount=(thinSegments.Length+thickSegments.Length)/4;
    }

    /// <summary>線分は世界の座標のパスにしておき、描画はキャンバスの行列で拡大する(基図と同じ)。</summary>
    public void Draw(SKCanvas canvas,MapView view,SKSize size){
        if(this.thinPath is null||this.thickPath is null){
            return;
        }
        var s=(float)view.WorldSize;
        this.thin.StrokeWidth=1.1f*view.PixelRatio/s;
        this.thick.StrokeWidth=1.8f*view.PixelRatio/s;
        var (ox,_)=view.Origin(size);
        var span=size.Width/view.WorldSize;
        for(var wrap=-1;wrap<=1;wrap++){
            //表示範囲に入る複製だけを描く
            if(wrap+1<ox||wrap>ox+span){
                continue;
            }
            canvas.Save();
            canvas.Concat(view.WorldToScreen(size,wrap));
            this.halo.StrokeWidth=3.2f*view.PixelRatio/s;
            canvas.DrawPath(this.thinPath,this.halo);
            this.halo.StrokeWidth=4.0f*view.PixelRatio/s;
            canvas.DrawPath(this.thickPath,this.halo);
            canvas.DrawPath(this.thinPath,this.thin);
            canvas.DrawPath(this.thickPath,this.thick);
            canvas.Restore();
        }
    }

    private static SKPath ToPath(float[] segments){
        using var builder=new SKPathBuilder();
        for(var i=0;i+3<segments.Length;i+=4){
            builder.MoveTo(segments[i],segments[i+1]);
            builder.LineTo(segments[i+2],segments[i+3]);
        }
        return builder.Detach();
    }

    /// <summary>マーチングスクエア。線分の端は世界の座標(Web メルカトル)。</summary>
    internal static (float[] Thin,float[] Thick) Contour(GridField field){
        var g=field.Geometry;
        var thin=new List<float>();
        var thick=new List<float>();
        var columns=g.Columns;
        var lastColumn=columns-1;
        if(g.WrapsLongitude){
            lastColumn=columns;
        }
        for(var r=0;r+1<g.Rows;r++){
            for(var c=0;c<lastColumn;c++){
                var c1=(c+1)%columns;
                var v00=field[c,r];
                var v10=field[c1,r];
                var v01=field[c,r+1];
                var v11=field[c1,r+1];
                if(float.IsNaN(v00)||float.IsNaN(v10)||float.IsNaN(v01)||float.IsNaN(v11)){
                    continue;
                }
                var min=Math.Min(Math.Min(v00,v10),Math.Min(v01,v11));
                var max=Math.Max(Math.Max(v00,v10),Math.Max(v01,v11));
                for(var level=Math.Ceiling(min/Interval)*Interval;level<=max;level+=Interval){
                    var target=thin;
                    if(Math.Abs(level%20)<1e-6){
                        target=thick;
                    }
                    Cell(g,c,r,v00,v10,v01,v11,level,target);
                }
            }
        }
        return ([..thin],[..thick]);
    }

    private static void Cell(GridGeometry g,int c,int r,double v00,double v10,double v01,double v11,double level,List<float> output){
        var index=0;
        if(v00>=level){
            index|=1;
        }
        if(v10>=level){
            index|=2;
        }
        if(v11>=level){
            index|=4;
        }
        if(v01>=level){
            index|=8;
        }
        if(index is 0 or 15){
            return;
        }
        //辺の交点(格子座標)。上 = r 行、下 = r+1 行
        (double X,double Y) Top()=>(c+Fraction(v00,v10,level),r);
        (double X,double Y) Bottom()=>(c+Fraction(v01,v11,level),r+1);
        (double X,double Y) Left()=>(c,r+Fraction(v00,v01,level));
        (double X,double Y) Right()=>(c+1,r+Fraction(v10,v11,level));
        void Add((double X,double Y) a,(double X,double Y) b){
            var (ax,ay)=World(g,a);
            var (bx,by)=World(g,b);
            //日付変更線をまたぐ線分は同じ側にそろえる(複製の描画で反対側にも出る)
            if(bx-ax>0.5){
                bx-=1;
            }else if(ax-bx>0.5){
                bx+=1;
            }
            output.Add((float)ax);
            output.Add((float)ay);
            output.Add((float)bx);
            output.Add((float)by);
        }
        switch(index){
            case 1:
            case 14:
                Add(Left(),Top());
                break;
            case 2:
            case 13:
                Add(Top(),Right());
                break;
            case 3:
            case 12:
                Add(Left(),Right());
                break;
            case 4:
            case 11:
                Add(Right(),Bottom());
                break;
            case 6:
            case 9:
                Add(Top(),Bottom());
                break;
            case 7:
            case 8:
                Add(Left(),Bottom());
                break;
            case 5:
                Add(Left(),Top());
                Add(Right(),Bottom());
                break;
            default:
                Add(Top(),Right());
                Add(Left(),Bottom());
                break;
        }
    }

    private static double Fraction(double a,double b,double level){
        var d=b-a;
        if(Math.Abs(d)<1e-9){
            return 0.5;
        }
        return Math.Clamp((level-a)/d,0,1);
    }

    /// <summary>格子座標 → 世界の座標。経度は −180〜180 に直してから投影する(格子は 0〜360)。</summary>
    private static (double X,double Y) World(GridGeometry g,(double X,double Y) p){
        var lon=g.WestLongitude+p.X*g.LongitudeStep;
        var lat=g.NorthLatitude-p.Y*g.LatitudeStep;
        var (x,y)=MapCamera.Project(lat,lon);
        x-=Math.Floor(x);
        return (x,y);
    }

    public void Dispose(){
        this.thinPath?.Dispose();
        this.thickPath?.Dispose();
        this.thin.Dispose();
        this.thick.Dispose();
        this.halo.Dispose();
    }
}
