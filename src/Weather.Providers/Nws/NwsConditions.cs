using System.Text.Json;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.Nws;

/// <summary>NWS の shortForecast / textDescription の語句解析(WeatherProviders.md「天気の判定」)。</summary>
public static class NwsForecastPhraseParser{
    private static readonly string[] ProbabilityWords=["slight chance","chance","likely","isolated","scattered","numerous","widespread","patchy","areas of","periods of","occasional","definite","frequent","intermittent"];

    public static CompositeCondition? Parse(string? text){
        if(string.IsNullOrWhiteSpace(text)){
            return null;
        }
        var parts=text.Split(" then ",2,StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var primary=ParsePart(parts[0]);
        if(primary is null){
            return null;
        }
        if(parts.Length>1&&ParsePart(parts[1]) is {} secondary){
            return new CompositeCondition(primary.Value,ConditionTransition.Later,secondary);
        }
        return new CompositeCondition(primary.Value);
    }

    public static WeatherCondition? ParsePart(string text){
        var t=" "+text.ToLowerInvariant().Replace('/',' ').Replace('-',' ')+" ";
        foreach(var word in ProbabilityWords){
            t=t.Replace(" "+word+" "," ",StringComparison.Ordinal);
        }
        var matched=false;
        var sky=SkyCover.Unknown;
        if(Has(t,"mostly sunny")||Has(t,"mostly clear")){
            sky=SkyCover.MostlyClear;
            matched=true;
        }else if(Has(t,"partly sunny")||Has(t,"partly cloudy")){
            sky=SkyCover.PartlyCloudy;
            matched=true;
        }else if(Has(t,"mostly cloudy")){
            sky=SkyCover.MostlyCloudy;
            matched=true;
        }else if(Has(t,"sunny")||Has(t,"clear")){
            sky=SkyCover.Clear;
            matched=true;
        }else if(Has(t,"fair")){
            sky=SkyCover.MostlyClear;
            matched=true;
        }else if(Has(t,"cloudy")||Has(t,"overcast")){
            sky=SkyCover.Overcast;
            matched=true;
        }

        var thunder=Has(t,"thunderstorm")||Has(t,"t storm")||Has(t,"thunder");
        var showery=Has(t,"shower")||thunder;
        var precipitation=PrecipitationType.None;
        if(Has(t,"freezing rain")||Has(t,"freezing drizzle")){
            precipitation=PrecipitationType.FreezingRain;
        }else if(Has(t,"sleet")||Has(t,"ice pellets")){
            precipitation=PrecipitationType.IcePellets;
        }else if(Has(t,"hail")){
            precipitation=PrecipitationType.Hail;
        }else if(Has(t,"rain")&&Has(t,"snow")){
            precipitation=PrecipitationType.RainAndSnow;
        }else if(Has(t,"snow")||Has(t,"flurries")){
            precipitation=PrecipitationType.Snow;
        }else if(Has(t,"drizzle")){
            precipitation=PrecipitationType.Drizzle;
        }else if(Has(t,"rain")||Has(t,"showers")||thunder){
            precipitation=PrecipitationType.Rain;
        }

        var intensity=PrecipitationIntensity.None;
        if(precipitation!=PrecipitationType.None){
            matched=true;
            intensity=PrecipitationIntensity.Moderate;
            if(Has(t,"heavy")){
                intensity=PrecipitationIntensity.Heavy;
            }else if(Has(t,"light")||precipitation==PrecipitationType.Drizzle){
                intensity=PrecipitationIntensity.Light;
            }
            if(sky==SkyCover.Unknown){
                if(showery){
                    sky=SkyCover.MostlyCloudy;
                }else{
                    sky=SkyCover.Overcast;
                }
            }
        }

        var obscuration=Obscuration.None;
        if(Has(t,"fog")){
            obscuration=Obscuration.Fog;
        }else if(Has(t,"mist")){
            obscuration=Obscuration.Mist;
        }else if(Has(t,"haze")){
            obscuration=Obscuration.Haze;
        }else if(Has(t,"smoke")){
            obscuration=Obscuration.Smoke;
        }else if(Has(t,"dust")||Has(t,"sand")){
            obscuration=Obscuration.Dust;
        }
        if(obscuration!=Obscuration.None){
            matched=true;
            if(sky==SkyCover.Unknown&&obscuration is Obscuration.Fog or Obscuration.Mist){
                sky=SkyCover.Overcast;
            }
        }

        var wind=Has(t,"breezy")||Has(t,"windy")||Has(t,"blustery");
        if(wind){
            matched=true;
        }
        if(!matched){
            return null;
        }
        return new WeatherCondition(sky,precipitation,intensity,showery&&precipitation!=PrecipitationType.None,thunder,wind,obscuration);
    }

    private static bool Has(string text,string word){
        return text.Contains(word,StringComparison.Ordinal);
    }
}

/// <summary>NWS グリッドの weather 値 → WeatherCondition(WeatherProviders.md 付録 B)。</summary>
public static class NwsGridWeather{
    public static readonly IReadOnlyList<string> KnownWeather=["blowing_dust","blowing_sand","blowing_snow","drizzle","fog","freezing_fog","freezing_drizzle","freezing_rain","freezing_spray","frost","hail","haze","ice_crystals","ice_fog","rain","rain_showers","sleet","smoke","snow","snow_showers","thunderstorms","volcanic_ash","water_spouts"];

    public static WeatherCondition? FromValues(JsonElement values,SkyCover sky,List<string> unknown){
        var precipitation=PrecipitationType.None;
        var intensity=PrecipitationIntensity.None;
        var showery=false;
        var thunder=false;
        var wind=false;
        var obscuration=Obscuration.None;
        var any=false;
        foreach(var item in values.EnumerateArray()){
            var weather=item.Str("weather");
            if(weather is null){
                continue;
            }
            any=true;
            var type=PrecipitationType.None;
            var itemIntensity=MapIntensity(item.Str("intensity"));
            switch(weather){
                case "rain":
                    type=PrecipitationType.Rain;
                    break;
                case "rain_showers":
                    type=PrecipitationType.Rain;
                    showery=true;
                    break;
                case "snow":
                    type=PrecipitationType.Snow;
                    break;
                case "snow_showers":
                    type=PrecipitationType.Snow;
                    showery=true;
                    break;
                case "sleet":
                    type=PrecipitationType.IcePellets;
                    break;
                case "freezing_rain":
                    type=PrecipitationType.FreezingRain;
                    break;
                case "freezing_drizzle":
                    type=PrecipitationType.FreezingRain;
                    itemIntensity=PrecipitationIntensity.Light;
                    break;
                case "drizzle":
                    type=PrecipitationType.Drizzle;
                    break;
                case "hail":
                    type=PrecipitationType.Hail;
                    break;
                case "thunderstorms":
                    type=PrecipitationType.Rain;
                    showery=true;
                    thunder=true;
                    break;
                case "fog":
                case "freezing_fog":
                case "ice_fog":
                    obscuration=Obscuration.Fog;
                    break;
                case "haze":
                    obscuration=Higher(obscuration,Obscuration.Haze);
                    break;
                case "smoke":
                    obscuration=Higher(obscuration,Obscuration.Smoke);
                    break;
                case "blowing_dust":
                case "blowing_sand":
                case "volcanic_ash":
                    obscuration=Higher(obscuration,Obscuration.Dust);
                    break;
                case "blowing_snow":
                    wind=true;
                    break;
                case "frost":
                case "ice_crystals":
                case "freezing_spray":
                case "water_spouts":
                    break;
                default:
                    unknown.Add(weather);
                    break;
            }
            foreach(var attribute in item.Items("attributes")){
                var a=attribute.GetString();
                if(a=="heavy_rain"){
                    itemIntensity=PrecipitationIntensity.Heavy;
                }else if(a is "gusty_wind" or "damaging_wind"){
                    wind=true;
                }
            }
            if(type!=PrecipitationType.None){
                if(Priority(type)>Priority(precipitation)){
                    precipitation=type;
                }
                if(itemIntensity==PrecipitationIntensity.None){
                    itemIntensity=PrecipitationIntensity.Moderate;
                }
                if(itemIntensity>intensity){
                    intensity=itemIntensity;
                }
            }
        }
        if(!any){
            return null;
        }
        if(precipitation==PrecipitationType.None){
            intensity=PrecipitationIntensity.None;
            showery=false;
        }
        return new WeatherCondition(sky,precipitation,intensity,showery,thunder,wind,obscuration);
    }

    private static PrecipitationIntensity MapIntensity(string? intensity){
        switch(intensity){
            case "very_light":
            case "light":
                return PrecipitationIntensity.Light;
            case "moderate":
                return PrecipitationIntensity.Moderate;
            case "heavy":
                return PrecipitationIntensity.Heavy;
            default:
                return PrecipitationIntensity.None;
        }
    }

    /// <summary>Hail > FreezingRain > IcePellets > Snow > Rain > Drizzle。</summary>
    private static int Priority(PrecipitationType type){
        switch(type){
            case PrecipitationType.Hail:
                return 7;
            case PrecipitationType.FreezingRain:
                return 6;
            case PrecipitationType.IcePellets:
                return 5;
            case PrecipitationType.RainAndSnow:
                return 4;
            case PrecipitationType.Snow:
                return 3;
            case PrecipitationType.Rain:
                return 2;
            case PrecipitationType.Drizzle:
                return 1;
            default:
                return 0;
        }
    }

    private static Obscuration Higher(Obscuration current,Obscuration candidate){
        if(current==Obscuration.Fog){
            return current;
        }
        return candidate;
    }
}

/// <summary>NWS の単位コード(wmoUnit:*)→ SI。</summary>
public static class NwsUnits{
    public static double? ToSi(double? value,string? unit){
        if(value is not {} v){
            return null;
        }
        switch(unit){
            case "wmoUnit:degF":
                return (v-32)*5/9;
            case "wmoUnit:km_h-1":
                return v/3.6;
            case "wmoUnit:Pa":
                return v/100;
            case "wmoUnit:km":
                return v*1000;
            default:
                return v;
        }
    }

    /// <summary>"7 km/h" / "10 to 15 km/h" / "5 mph" → m/s(下限・上限)。</summary>
    public static (double Low,double High)? ParseWindSpeed(string? text){
        if(string.IsNullOrWhiteSpace(text)){
            return null;
        }
        var tokens=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var numbers=new List<double>();
        foreach(var token in tokens){
            if(double.TryParse(token,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)){
                numbers.Add(n);
            }
        }
        if(numbers.Count==0){
            return null;
        }
        var factor=1/3.6;
        if(text.Contains("mph",StringComparison.OrdinalIgnoreCase)){
            factor=0.44704;
        }
        var low=numbers[0]*factor;
        var high=numbers[^1]*factor;
        return (low,high);
    }
}
