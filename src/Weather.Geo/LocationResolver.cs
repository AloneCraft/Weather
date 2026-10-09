using System.Globalization;
using Weather.Core;

namespace Weather.Geo;

/// <summary>
/// 地点の解決(CacheAndRouting.md「解決手順」)。
/// 日本周辺域 → 気象庁区域(内側 / 20 km 以内の最寄り / 対象外)、それ以外 → 国判定と最寄り都市。
/// </summary>
public sealed class LocationResolver(GeoDatabase database):ILocationResolver{
    public const double NearestAreaKm=20;
    public const double NearestPlaceKm=50;
    private const string JapanTimeZone="Asia/Tokyo";

    public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
        return ValueTask.FromResult(this.Resolve(point));
    }

    public ResolvedLocation Resolve(GeoPoint point){
        var lat=point.Latitude;
        var lon=point.Longitude;
        var rounded=point.RoundForRequest();

        var inside=database.FindJmaAreaContaining(lat,lon);
        if(inside is not null){
            return new ResolvedLocation{
                Point=rounded,
                CountryCode="JP",
                DisplayName=inside.Name,
                AdminName=inside.PrefectureName,
                TimeZoneId=JapanTimeZone,
                JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Inside,inside.Code,0),
            };
        }
        if(database.JapanMask.Contains(lat,lon)){
            var (nearest,distance)=database.FindNearestJmaArea(lat,lon,NearestAreaKm);
            if(nearest is not null&&distance<=NearestAreaKm){
                return new ResolvedLocation{
                    Point=rounded,
                    CountryCode="JP",
                    DisplayName=nearest.Name,
                    AdminName=nearest.PrefectureName,
                    TimeZoneId=JapanTimeZone,
                    JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Nearest,nearest.Code,distance),
                };
            }
            return new ResolvedLocation{
                Point=rounded,
                CountryCode="JP",
                DisplayName=FormatCoordinates(rounded),
                AdminName="日本周辺(気象庁の予報区域外)",
                TimeZoneId=JapanTimeZone,
                JmaArea=new JmaAreaMatch(JmaAreaMatchKind.OutOfCoverage,null,FiniteOrInfinity(distance)),
            };
        }

        var country=database.FindCountry(lat,lon);
        var countryCode="XX";
        if(country is not null){
            countryCode=country.Iso2;
        }
        //国が判明していれば、その国の都市だけを候補にする(国境の向こうの都市の名前・行政区・タイムゾーンを出さない)
        var place=database.FindNearestPlace(lat,lon,NearestPlaceKm,country?.Iso2);
        string displayName;
        string? admin;
        if(place is not null){
            displayName=place.JaName??place.Name;
            admin=place.Admin1;
            if(country is not null){
                admin=JoinNonEmpty(admin,country.NameJa);
            }
        }else{
            displayName=FormatCoordinates(rounded);
            admin=country?.NameJa;
        }
        return new ResolvedLocation{
            Point=rounded,
            CountryCode=countryCode,
            DisplayName=displayName,
            AdminName=admin,
            TimeZoneId=this.ResolveTimeZone(lat,lon,country?.Iso2),
        };
    }

    /// <summary>最寄り都市の timezone。近くに都市がない海上、または端末が知らない ID(tzdata が古い端末の新しい IANA ID)は経度から Etc/GMT±n を使う。</summary>
    private string ResolveTimeZone(double lat,double lon,string? country){
        if(database.FindNearestPlace(lat,lon,800,country) is {} place&&IsKnownZone(place.TimeZone)){
            return place.TimeZone;
        }
        var offset=(int)Math.Round(lon/15);
        if(offset==0){
            return "Etc/UTC";
        }
        //Etc/GMT の符号は逆(東経 = マイナス)
        if(offset>0){
            return string.Create(CultureInfo.InvariantCulture,$"Etc/GMT-{offset}");
        }
        return string.Create(CultureInfo.InvariantCulture,$"Etc/GMT+{-offset}");
    }

    /// <summary>この端末で解決できる timezone か。解決できない ID は下流(予報の現地日付・表示時刻)で黙って UTC にされるため、返さない。</summary>
    private static bool IsKnownZone(string zoneId){
        try{
            TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return true;
        }catch(Exception ex) when(ex is TimeZoneNotFoundException or InvalidTimeZoneException){
            return false;
        }
    }

    /// <summary>探索範囲内に区域がない場合は無限大(20 km より遠い)とする。</summary>
    private static double FiniteOrInfinity(double distance){
        if(distance>=double.MaxValue){
            return double.PositiveInfinity;
        }
        return distance;
    }

    private static string FormatCoordinates(GeoPoint point){
        var ns="N";
        if(point.Latitude<0){
            ns="S";
        }
        var ew="E";
        if(point.Longitude<0){
            ew="W";
        }
        return string.Create(CultureInfo.InvariantCulture,$"{Math.Abs(point.Latitude):0.00}°{ns} {Math.Abs(point.Longitude):0.00}°{ew}");
    }

    private static string? JoinNonEmpty(string? a,string? b){
        if(string.IsNullOrEmpty(a)){
            return b;
        }
        if(string.IsNullOrEmpty(b)){
            return a;
        }
        return a+"、"+b;
    }
}
