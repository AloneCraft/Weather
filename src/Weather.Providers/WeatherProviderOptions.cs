namespace Weather.Providers;

public sealed class WeatherProviderOptions{
    /// <summary>
    /// 全 Provider に付与する User-Agent(WeatherProviders.md ガードレール 4)。
    /// 形式: WeatherApp/{version} github.com/{owner}/{repo}。リポジトリ公開時に確定する。
    /// </summary>
    public string UserAgent{get;set;}="WeatherApp/0.1 github.com/AloneCraft/Weather";

    public TimeSpan Timeout{get;set;}=TimeSpan.FromSeconds(15);

    /// <summary>HTTP キャッシュの容量上限(CacheAndRouting.md)。地図(GFS 0.5° は 1 要素 160〜290KB)を含めて 200MB。</summary>
    public long CacheMaxBytes{get;set;}=200L*1024*1024;

    public TimeSpan CacheMaxUnused{get;set;}=TimeSpan.FromDays(14);
}
