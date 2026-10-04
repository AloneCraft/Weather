using System.Globalization;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.Jma;

/// <summary>気象庁 bosai の URL(2026-10-04 確認。公開仕様ではないため一元管理する)。</summary>
internal static class JmaEndpoints{
    public static readonly Uri BaseUri=new("https://www.jma.go.jp/bosai/");
    public static readonly Uri Area=new(BaseUri,"common/const/area.json");
    public static readonly Uri ForecastArea=new(BaseUri,"forecast/const/forecast_area.json");
    public static readonly Uri AmedasTable=new(BaseUri,"amedas/const/amedastable.json");
    public static readonly Uri AmedasLatestTime=new(BaseUri,"amedas/data/latest_time.txt");

    public static Uri Forecast(string office){
        return new Uri(BaseUri,$"forecast/data/forecast/{office}.json");
    }

    public static Uri TimeSeries(string class10){
        return new Uri(BaseUri,$"jmatile/data/wdist/VPFD/{class10}.json");
    }

    public static Uri Warning(string office){
        return new Uri(BaseUri,$"warning/data/r8/{office}.json");
    }

    /// <summary>アメダス地点の 3 時間ブロック(JST の 0,3,…,21 時始まり)。</summary>
    public static Uri AmedasPoint(string station,DateTimeOffset blockStartJst){
        var name=blockStartJst.ToString("yyyyMMdd'_'HH",CultureInfo.InvariantCulture);
        return new Uri(BaseUri,$"amedas/data/point/{station}/{name}.json");
    }

    public static Uri ForecastPage(string office){
        return new Uri($"https://www.jma.go.jp/bosai/forecast/#area_type=offices&area_code={office}");
    }

    public static Uri WarningPage(string class20){
        return new Uri($"https://www.jma.go.jp/bosai/warning/#area_type=class20s&area_code={class20}");
    }

    public static Uri AmedasPage(string station){
        return new Uri($"https://www.jma.go.jp/bosai/amedas/#amdno={station}");
    }
}

internal static class JmaAttribution{
    public static readonly TimeSpan Jst=TimeSpan.FromHours(9);
    public static readonly LicenseInfo License=new("公共データ利用規約(第1.0版)/ 出典:気象庁ホームページ",new Uri("https://www.jma.go.jp/jma/kishou/info/coment.html"));

    public static SourceAttribution Create(string product,string? office,DateTimeOffset? issuedAt,FetchResult fetch,Uri page){
        return new SourceAttribution{
            Provider=ProviderId.Jma,
            AgencyName="気象庁",
            ProductName=product,
            PublishingOffice=office,
            IssuedAt=issuedAt,
            RetrievedAt=fetch.RetrievedAt,
            License=License,
            SourceUrl=page,
            IsStale=fetch.IsStale,
        };
    }

    public static DateOnly JstDate(DateTimeOffset time){
        return DateOnly.FromDateTime(time.ToOffset(Jst).DateTime);
    }
}
