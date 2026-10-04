namespace Weather.Providers.Http;

/// <summary>URL ごとのキャッシュ方針(CacheAndRouting.md)。</summary>
public sealed record HttpCacheRule(TimeSpan MaxStale,TimeSpan MinimumFreshness);

public sealed class HttpCachePolicy{
    public static TimeSpan Unlimited{get;}=TimeSpan.MaxValue;

    /// <summary>警報は古いものを表示すると危険なため許容期間を短くする。</summary>
    public HttpCacheRule Resolve(Uri uri){
        ArgumentNullException.ThrowIfNull(uri);
        var path=uri.AbsolutePath;
        if(path.Contains("/bosai/warning/",StringComparison.Ordinal)||path.StartsWith("/alerts",StringComparison.Ordinal)){
            return new HttpCacheRule(TimeSpan.FromHours(6),TimeSpan.Zero);
        }
        if(path.Contains("/const/",StringComparison.Ordinal)){
            return new HttpCacheRule(Unlimited,TimeSpan.FromDays(7));
        }
        return new HttpCacheRule(TimeSpan.FromDays(7),TimeSpan.Zero);
    }
}
