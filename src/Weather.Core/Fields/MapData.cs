using System.Globalization;

namespace Weather.Core;

/// <summary>凡例の 1 区分([Lower, Upper))。色は ARGB。Key は区分の識別子(画面の文言は Presentation が作る)。</summary>
public sealed record LegendClass(double? Lower,double? Upper,uint Color,string Key);

/// <summary>凡例。区分は値の小さい順。Smooth は区分の色の間を連続的に補間して描く(風速など気象庁の凡例と合わせない層)。</summary>
public sealed class Legend{
    public Legend(string unit,IReadOnlyList<LegendClass> classes,bool smooth=false){
        ArgumentNullException.ThrowIfNull(classes);
        if(classes.Count==0){
            throw new ArgumentException("凡例の区分がありません。",nameof(classes));
        }
        this.Unit=unit;
        this.Classes=[..classes];
        this.Smooth=smooth;
    }

    public string Unit{get;}
    public IReadOnlyList<LegendClass> Classes{get;}
    public bool Smooth{get;}

    /// <summary>値の区分。NaN と最初の区分の下限未満は null(透明)。</summary>
    public LegendClass? Classify(double value){
        if(double.IsNaN(value)){
            return null;
        }
        foreach(var c in this.Classes){
            if((c.Lower is null||value>=c.Lower)&&(c.Upper is null||value<c.Upper)){
                return c;
            }
        }
        return null;
    }

    /// <summary>
    /// 画素の色から区分を引く(気象庁のタイルの値の読み取り)。透明なら null。
    /// 凡例画像とタイルで色が数段階違う場合がある(例: 255,245,0 と 250,245,0)ため、近い色を許容する。
    /// </summary>
    public LegendClass? MatchColor(uint argb,int tolerance=24){
        if((argb>>24)==0){
            return null;
        }
        LegendClass? best=null;
        var bestDistance=int.MaxValue;
        foreach(var c in this.Classes){
            var d=Distance(argb,c.Color);
            if(d<bestDistance){
                best=c;
                bestDistance=d;
            }
        }
        if(bestDistance>tolerance*tolerance*3){
            return null;
        }
        return best;
    }

    private static int Distance(uint a,uint b){
        var dr=(int)((a>>16)&0xFF)-(int)((b>>16)&0xFF);
        var dg=(int)((a>>8)&0xFF)-(int)((b>>8)&0xFF);
        var db=(int)(a&0xFF)-(int)(b&0xFF);
        return dr*dr+dg*dg+db*db;
    }
}

/// <summary>
/// 地図の凡例。気象庁の凡例は jmatile の凡例画像・設定(2026-10-04 取得)の色をそのまま使う。
/// GFS の降水・気温も同じ区分と色で描き、日本周辺(気象庁)との継ぎ目をそろえる。
/// </summary>
public static class MapLegends{
    /// <summary>降水強度(雨雲の動き・今後の雨。mm/h)。GFS は 0.1 mm/h 未満を描かず、1 mm/h 未満は半透明にする。</summary>
    public static Legend RainRate{get;}=new("mm/h",[
        new(0.1,1,0x99F2F2FF,"0.1"),
        new(1,5,0xFFA0D2FF,"1"),
        new(5,10,0xFF218CFF,"5"),
        new(10,20,0xFF0041FF,"10"),
        new(20,30,0xFFFFF500,"20"),
        new(30,50,0xFFFF9900,"30"),
        new(50,80,0xFFFF2800,"50"),
        new(80,null,0xFFB40068,"80"),
    ]);

    /// <summary>3 時間降水量(天気分布予報。mm/3h)。</summary>
    public static Legend Rain3h{get;}=new("mm/3h",[
        new(1,5,0xFFA0D2FF,"1"),
        new(5,10,0xFF218CFF,"5"),
        new(10,15,0xFF0041FF,"10"),
        new(15,20,0xFFFFF500,"15"),
        new(20,null,0xFFFF9900,"20"),
    ]);

    /// <summary>気温(天気分布予報。℃)。</summary>
    public static Legend Temperature{get;}=new("℃",[
        new(null,-25,0xFFD8D6E4,"below-25"),
        new(-25,-20,0xFFA8A3C5,"-25"),
        new(-20,-15,0xFF776FA4,"-20"),
        new(-15,-10,0xFF4D4471,"-15"),
        new(-10,-5,0xFF002080,"-10"),
        new(-5,0,0xFF0041FF,"-5"),
        new(0,5,0xFF0096FF,"0"),
        new(5,10,0xFFB9EBFF,"5"),
        new(10,15,0xFFFFFFF0,"10"),
        new(15,20,0xFFFFFF96,"15"),
        new(20,25,0xFFFAF500,"20"),
        new(25,30,0xFFFF9900,"25"),
        new(30,35,0xFFFF2800,"30"),
        new(35,40,0xFFB40068,"35"),
        new(40,null,0xFF50002E,"40"),
    ]);

    /// <summary>天気(天気分布予報。区分のみで値はない)。</summary>
    public static Legend Weather{get;}=new("",[
        new(null,null,0xFFFFAA00,"clear"),
        new(null,null,0xFFAAAAAA,"cloudy"),
        new(null,null,0xFF0041FF,"rain"),
        new(null,null,0xFFA0D2FF,"rainOrSnow"),
        new(null,null,0xFFF2F2FF,"snow"),
    ]);

    /// <summary>風速(GFS。m/s)。気象庁に対応する面の凡例がないため、連続的な配色にする。</summary>
    public static Legend WindSpeed{get;}=new("m/s",[
        new(0,3,0xFF3B5DA8,"0"),
        new(3,6,0xFF3E8FC7,"3"),
        new(6,9,0xFF3FB7A5,"6"),
        new(9,12,0xFF6CC55B,"9"),
        new(12,15,0xFFD7D14B,"12"),
        new(15,20,0xFFE8973C,"15"),
        new(20,25,0xFFD9483B,"20"),
        new(25,null,0xFFA12E7E,"25"),
    ],smooth:true);

    /// <summary>雲量(GFS。%)。白の濃さで表す。</summary>
    public static Legend CloudCover{get;}=new("%",[
        new(10,30,0x40FFFFFF,"10"),
        new(30,50,0x70FFFFFF,"30"),
        new(50,70,0xA0FFFFFF,"50"),
        new(70,90,0xC8FFFFFF,"70"),
        new(90,null,0xE6FFFFFF,"90"),
    ],smooth:true);
}

/// <summary>気象庁の地図タイルのプロダクト(jmatile)。</summary>
public enum JmaTileProduct{Nowcast,RainForecast,DistributionForecast,MarineForecast}

/// <summary>
/// 気象庁の地図タイルの 1 層(1 要素・1 時刻)。URL の形・ズームは jmatile の設定(2026-10-04 確認)。
/// タイルは配色を変えずにそのまま表示する(方針 5)。
/// </summary>
public sealed record JmaTileLayer{
    public const string Root="https://www.jma.go.jp/bosai/jmatile/data/";

    public required JmaTileProduct Product{get;init;}

    /// <summary>要素(hrpns・rasrf・temp・wm・r3・ws)。</summary>
    public required string Element{get;init;}

    public required DateTimeOffset BaseTime{get;init;}
    public required DateTimeOffset ValidTime{get;init;}
    public required int MinZoom{get;init;}

    /// <summary>画像が実在する最大ズーム(これより拡大するときは拡大して描く)。</summary>
    public required int MaxZoom{get;init;}

    /// <summary>偶数ズームだけ画像がある(雨雲・今後の雨・天気分布予報)。</summary>
    public bool EvenZoomOnly{get;init;}

    /// <summary>画素を補間せずに描く(気象庁の表示と同じ)。</summary>
    public bool Pixelated{get;init;}

    public required Legend Legend{get;init;}
    public required SourceAttribution Source{get;init;}

    public string ProductPath{
        get{
            switch(this.Product){
                case JmaTileProduct.Nowcast:
                    return "nowc";
                case JmaTileProduct.RainForecast:
                    return "rasrf";
                case JmaTileProduct.DistributionForecast:
                    return "wdist";
                default:
                    return "umimesh";
            }
        }
    }

    public Uri TileUri(int z,int x,int y){
        return new Uri(string.Create(CultureInfo.InvariantCulture,$"{Root}{this.ProductPath}/{this.BaseTime.UtcDateTime:yyyyMMddHHmmss}/none/{this.ValidTime.UtcDateTime:yyyyMMddHHmmss}/surf/{this.Element}/{z}/{x}/{y}.png"));
    }

    /// <summary>表示のズーム(小数)に対して読み込むタイルのズーム。実在するズームに丸める。</summary>
    public int TileZoomFor(double viewZoom){
        var z=(int)Math.Floor(viewZoom+0.25);
        if(this.EvenZoomOnly&&z%2!=0){
            z--;
        }
        return Math.Clamp(z,this.MinZoom,this.MaxZoom);
    }
}

public enum ArrowKind{Observation,Forecast}

/// <summary>風の矢印(風が吹いてくる方位。北 = 0°、時計回り)。風速がない場合は方位のみ(海上分布予報)。</summary>
public readonly record struct WindArrow(GeoPoint Point,double FromDirectionDeg,double? SpeedMs);

/// <summary>日本周辺の風の矢印(アメダスの観測・海上分布予報)。値はそのまま描き、補間しない。</summary>
public sealed record WindArrowSet(IReadOnlyList<WindArrow> Arrows,ArrowKind Kind,DateTimeOffset ValidTime,SourceAttribution Source);

/// <summary>地点の値(アメダスの気温など)。</summary>
public readonly record struct PointValue(GeoPoint Point,double Value);

/// <summary>日本周辺の地点の観測値(アメダス)。値はそのまま描き、補間しない。</summary>
public sealed record PointValueSet(IReadOnlyList<PointValue> Points,FieldQuantity Quantity,DateTimeOffset ValidTime,SourceAttribution Source);

/// <summary>日本周辺の表示状態。</summary>
public enum JapanCoverage{
    /// <summary>気象庁のデータを表示している。</summary>
    Available,

    /// <summary>この層・時刻に対応する気象庁のデータがない(例: 未来の時刻の陸上の風)。</summary>
    NotAvailable,

    /// <summary>気象庁の予報期間を過ぎている。</summary>
    OutOfRange,
}

/// <summary>地図の 1 コマ(1 層・1 時刻)。GFS の格子は日本周辺が NaN(方針 5)。</summary>
public sealed record MapFrame{
    public required FieldLayer Layer{get;init;}
    public required DateTimeOffset Time{get;init;}

    /// <summary>色で塗る値(風速・降水強度・気温・雲量)。</summary>
    public GridField? Scalar{get;init;}

    public WindField? Wind{get;init;}

    /// <summary>等圧線の海面気圧(雲・気圧の層)。</summary>
    public GridField? Pressure{get;init;}

    public IReadOnlyList<JmaTileLayer> Tiles{get;init;}=[];
    public WindArrowSet? Arrows{get;init;}

    /// <summary>地点の観測値(現在時刻の気温の層のアメダス)。</summary>
    public PointValueSet? Points{get;init;}

    public required JapanCoverage Japan{get;init;}

    /// <summary>主な凡例(GFS と気象庁で共通。降水の 3 時間量など別の凡例のタイルは JmaTileLayer.Legend を使う)。</summary>
    public Legend? Legend{get;init;}

    /// <summary>この画面に表示しているデータの出典(方針 7)。</summary>
    public IReadOnlyList<SourceAttribution> Sources{get;init;}=[];

    /// <summary>取得できなかった部分(例: 「GFS」「雨雲」)。</summary>
    public IReadOnlyList<string> Issues{get;init;}=[];
}

/// <summary>各データ源の有効時刻(時間軸を作るため)。</summary>
public sealed record MapAvailability{
    public required DateTimeOffset RetrievedAt{get;init;}
    public IReadOnlyList<DateTimeOffset> Nowcast{get;init;}=[];
    public IReadOnlyList<DateTimeOffset> RainForecast{get;init;}=[];
    public IReadOnlyList<DateTimeOffset> Distribution{get;init;}=[];
    public IReadOnlyList<DateTimeOffset> Marine{get;init;}=[];
    public IReadOnlyList<DateTimeOffset> Gfs{get;init;}=[];
    public DateTimeOffset? GfsReferenceTime{get;init;}
    public DateTimeOffset? AmedasTime{get;init;}
}

/// <summary>地図のデータ(Providers が実装)。</summary>
public interface IMapDataService{
    ValueTask<MapAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);
    ValueTask<MapFrame> GetFrameAsync(FieldLayer layer,DateTimeOffset time,CancellationToken cancellationToken);

    /// <summary>気象庁のタイル(PNG)。なければ null。</summary>
    ValueTask<byte[]?> GetTileAsync(JmaTileLayer layer,int z,int x,int y,CancellationToken cancellationToken);
}
