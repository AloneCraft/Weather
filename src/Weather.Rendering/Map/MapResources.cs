using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SkiaSharp;
using Weather.Core;
using Weather.Geo;
using Weather.Geo.Data;

namespace Weather.Rendering.Map;

/// <summary>
/// 格子の値を 0〜1 に正規化する方法(テクスチャに入れるため)。降水は弱い雨の区分を細かく見分けるため対数にする。
/// </summary>
public readonly record struct FieldEncoding(double Min,double Max,bool Logarithmic){
    public static FieldEncoding For(FieldQuantity quantity){
        switch(quantity){
            case FieldQuantity.TemperatureC:
                return new FieldEncoding(-45,50,false);
            case FieldQuantity.PrecipitationMmPerHour:
                return new FieldEncoding(0,150,true);
            case FieldQuantity.CloudCoverPercent:
                return new FieldEncoding(0,100,false);
            case FieldQuantity.PressureHpa:
                return new FieldEncoding(900,1080,false);
            default:
                return new FieldEncoding(0,40,false);
        }
    }

    public float Encode(double value){
        double t;
        if(this.Logarithmic){
            t=Math.Log2(1+Math.Max(0,value))/Math.Log2(1+this.Max);
        }else{
            t=(value-this.Min)/(this.Max-this.Min);
        }
        return (float)Math.Clamp(t,0,1);
    }

    public double Decode(double t){
        if(this.Logarithmic){
            return Math.Pow(2,t*Math.Log2(1+this.Max))-1;
        }
        return this.Min+t*(this.Max-this.Min);
    }
}

/// <summary>凡例 → 256 色のパレット画像(格子レイヤーのシェーダーが値で引く)。凡例ごとに一度だけ作る。</summary>
public static class MapPalettes{
    private static readonly ConcurrentDictionary<(Legend,FieldEncoding),SKImage> Cache=new();

    public static SKImage Get(Legend legend,FieldEncoding encoding){
        return Cache.GetOrAdd((legend,encoding),static key=>Create(key.Item1,key.Item2));
    }

    /// <summary>値 → 色。区分の下限未満は透明。Smooth の凡例は区分の下限どうしの間を補間する。</summary>
    public static SKColor ColorFor(Legend legend,double value){
        ArgumentNullException.ThrowIfNull(legend);
        if(!legend.Smooth){
            if(legend.Classify(value) is {} c){
                return new SKColor(c.Color);
            }
            return SKColors.Transparent;
        }
        //Smooth の凡例は各区分の下限に色を置き、その間を補間する(下限は必須)
        var classes=legend.Classes;
        if(double.IsNaN(value)||value<(classes[0].Lower??double.NegativeInfinity)){
            return SKColors.Transparent;
        }
        for(var i=0;i+1<classes.Count;i++){
            var from=classes[i].Lower??double.NegativeInfinity;
            var next=classes[i+1].Lower??double.PositiveInfinity;
            if(value<next){
                var t=0.0;
                if(!double.IsInfinity(from)&&!double.IsInfinity(next)){
                    t=(value-from)/(next-from);
                }
                return ColorMath.Lerp(new SKColor(classes[i].Color),new SKColor(classes[i+1].Color),t);
            }
        }
        return new SKColor(classes[^1].Color);
    }

    private static SKImage Create(Legend legend,FieldEncoding encoding){
        using var bitmap=new SKBitmap(new SKImageInfo(256,1,SKColorType.Rgba8888,SKAlphaType.Unpremul));
        for(var i=0;i<256;i++){
            bitmap.SetPixel(i,0,ColorFor(legend,encoding.Decode(i/255.0)));
        }
        return SKImage.FromBitmap(bitmap);
    }
}

/// <summary>
/// 日本周辺のマスク(Geo の 0.05° のビットマスク。地点の解決と同じ範囲)を画像にしたもの。行 0 が北端。
/// 粒子・吹き出しの判定にも同じマスクを使う。
/// </summary>
public sealed class JapanMaskTexture{
    private static readonly ConcurrentDictionary<GeoDatabase,JapanMaskTexture> Instances=new();

    private JapanMaskTexture(JapanAreaMask mask){
        this.Mask=mask;
        var alpha=new byte[mask.Rows*mask.Columns];
        for(var r=0;r<mask.Rows;r++){
            //マスクの行 0 は南端。画像は北端から並べる
            var lat=mask.MinLat+(r+0.5)*mask.Step;
            var row=mask.Rows-1-r;
            for(var c=0;c<mask.Columns;c++){
                var lon=mask.MinLon+(c+0.5)*mask.Step;
                if(mask.Contains(lat,lon)){
                    alpha[row*mask.Columns+c]=255;
                }
            }
        }
        this.Image=SKImage.FromPixelCopy(new SKImageInfo(mask.Columns,mask.Rows,SKColorType.Alpha8,SKAlphaType.Premul),alpha,mask.Columns);
        this.North=mask.MinLat+mask.Rows*mask.Step;
    }

    public static JapanMaskTexture For(GeoDatabase database){
        ArgumentNullException.ThrowIfNull(database);
        return Instances.GetOrAdd(database,static db=>new JapanMaskTexture(db.JapanMask));
    }

    public JapanAreaMask Mask{get;}
    public SKImage Image{get;}
    public double North{get;}

    /// <summary>シェーダーの maskRect(西端の経度・北端の緯度・経度と緯度の間隔)。</summary>
    public float[] Rect=>[(float)this.Mask.MinLon,(float)this.North,(float)this.Mask.Step,(float)this.Mask.Step];

    public float[] Size=>[this.Mask.Columns,this.Mask.Rows];

    public bool Contains(double latitude,double longitude){
        return this.Mask.Contains(latitude,longitude);
    }
}

/// <summary>格子 → RgbaF16 の画像(r = 正規化した値、a = 有効)。欠損・日本周辺の NaN は透明。</summary>
public static class FieldTextures{
    public static SKImage Create(GridField field,FieldEncoding encoding){
        ArgumentNullException.ThrowIfNull(field);
        var g=field.Geometry;
        var pixels=new Half[g.Count*4];
        var values=field.Values;
        for(var i=0;i<values.Length;i++){
            var v=values[i];
            if(float.IsNaN(v)){
                continue;
            }
            pixels[i*4]=(Half)encoding.Encode(v);
            pixels[i*4+3]=Half.One;
        }
        var bytes=MemoryMarshal.AsBytes(pixels.AsSpan());
        return SKImage.FromPixelCopy(new SKImageInfo(g.Columns,g.Rows,SKColorType.RgbaF16,SKAlphaType.Premul),bytes,g.Columns*8);
    }
}
