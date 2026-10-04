using Weather.Core;

namespace Weather.Providers.Jma;

internal sealed record JmaWeatherCode(string Name,string EnglishName,CompositeCondition Condition);

/// <summary>気象庁の天気コード → 名称・CompositeCondition(WeatherProviders.md 付録 A)。</summary>
internal static partial class JmaWeatherCodes{
    public static IReadOnlyCollection<string> Codes=>Table.Keys;

    public static JmaWeatherCode? Find(string? code){
        if(code is null){
            return null;
        }
        Table.TryGetValue(code,out var value);
        return value;
    }

    /// <summary>時系列予報の天気(単語)。区分の一覧は要確認(WeatherProviders.md)。</summary>
    public static CompositeCondition? FromTimeSeriesWord(string? word){
        switch(word){
            case "晴れ":
                return new WeatherCondition(SkyCover.Clear);
            case "くもり":
                return new WeatherCondition(SkyCover.Overcast);
            case "雨":
                return new WeatherCondition(SkyCover.Overcast,PrecipitationType.Rain,PrecipitationIntensity.Moderate);
            case "雨または雪":
            case "雪または雨":
            case "みぞれ":
                return new WeatherCondition(SkyCover.Overcast,PrecipitationType.RainAndSnow,PrecipitationIntensity.Moderate);
            case "雪":
                return new WeatherCondition(SkyCover.Overcast,PrecipitationType.Snow,PrecipitationIntensity.Moderate);
            default:
                return null;
        }
    }
}
