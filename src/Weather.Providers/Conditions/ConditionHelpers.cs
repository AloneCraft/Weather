using Weather.Core;

namespace Weather.Providers.Conditions;

/// <summary>雲量%→SkyCover(オクタ基準。CacheAndRouting / WeatherProviders.md)。</summary>
public static class SkyCoverClassifier{
    public static SkyCover FromPercent(double? percent){
        if(percent is not {} p){
            return SkyCover.Unknown;
        }
        if(p<=12.5){
            return SkyCover.Clear;
        }
        if(p<=37.5){
            return SkyCover.MostlyClear;
        }
        if(p<=62.5){
            return SkyCover.PartlyCloudy;
        }
        if(p<=87.5){
            return SkyCover.MostlyCloudy;
        }
        return SkyCover.Overcast;
    }
}

/// <summary>16 方位(日本語・英語)↔ 度。</summary>
public static class Compass{
    private static readonly string[] Japanese=["北","北北東","北東","東北東","東","東南東","南東","南南東","南","南南西","南西","西南西","西","西北西","北西","北北西"];
    private static readonly string[] English=["N","NNE","NE","ENE","E","ESE","SE","SSE","S","SSW","SW","WSW","W","WNW","NW","NNW"];

    public static double? FromJapanese(string? text){
        return Find(Japanese,text);
    }

    public static double? FromEnglish(string? text){
        return Find(English,text);
    }

    /// <summary>アメダスの風向コード(1〜16、16=北、0=静穏)。静穏は null。</summary>
    public static double? FromAmedasCode(int code){
        if(code<=0||code>16){
            return null;
        }
        return GeoMath.NormalizeDegrees(code*22.5);
    }

    public static string ToJapanese(double degrees){
        return Japanese[Index(degrees)];
    }

    public static string ToEnglish(double degrees){
        return English[Index(degrees)];
    }

    private static int Index(double degrees){
        return (int)Math.Round(GeoMath.NormalizeDegrees(degrees)/22.5,MidpointRounding.AwayFromZero)%16;
    }

    private static double? Find(string[] names,string? text){
        if(string.IsNullOrWhiteSpace(text)){
            return null;
        }
        var trimmed=text.Trim();
        for(var i=0;i<names.Length;i++){
            if(string.Equals(names[i],trimmed,StringComparison.OrdinalIgnoreCase)){
                return i*22.5;
            }
        }
        return null;
    }
}
