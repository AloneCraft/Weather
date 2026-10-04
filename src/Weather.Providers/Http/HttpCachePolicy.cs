namespace Weather.Providers.Http;

/// <summary>URL ごとのキャッシュ方針(CacheAndRouting.md)。</summary>
public sealed record HttpCacheRule(TimeSpan MaxStale,TimeSpan MinimumFreshness);

public sealed class HttpCachePolicy{
    public const string GfsHost="noaa-gfs-bdp-pds.s3.amazonaws.com";

    public static TimeSpan Unlimited{get;}=TimeSpan.MaxValue;

    /// <summary>
    /// 警報は古いものを表示すると危険なため許容期間を短くする。
    /// GFS のファイルと気象庁のタイル(初期時刻・有効時刻ごとの URL)は内容が変わらないため、取得後は再検証しない。
    /// </summary>
    public HttpCacheRule Resolve(Uri uri){
        ArgumentNullException.ThrowIfNull(uri);
        var path=uri.AbsolutePath;
        if(uri.Host==GfsHost){
            return new HttpCacheRule(TimeSpan.FromDays(7),TimeSpan.FromDays(7));
        }
        if(path.Contains("/bosai/jmatile/data/",StringComparison.Ordinal)&&(path.EndsWith(".png",StringComparison.Ordinal)||path.EndsWith(".geojson",StringComparison.Ordinal))){
            return new HttpCacheRule(TimeSpan.FromDays(1),TimeSpan.FromDays(1));
        }
        if(path.Contains("/bosai/amedas/data/map/",StringComparison.Ordinal)){
            return new HttpCacheRule(TimeSpan.FromDays(1),TimeSpan.FromDays(1));
        }
        if(path.Contains("/bosai/warning/",StringComparison.Ordinal)||path.StartsWith("/alerts",StringComparison.Ordinal)){
            return new HttpCacheRule(TimeSpan.FromHours(6),TimeSpan.Zero);
        }
        if(path.Contains("/const/",StringComparison.Ordinal)){
            return new HttpCacheRule(Unlimited,TimeSpan.FromDays(7));
        }
        return new HttpCacheRule(TimeSpan.FromDays(7),TimeSpan.Zero);
    }
}
