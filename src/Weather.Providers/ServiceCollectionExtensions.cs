using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Http;
using Weather.Providers.Jma;
using Weather.Providers.MetNorway;
using Weather.Providers.Nws;
using Weather.Providers.Routing;

namespace Weather.Providers;

public static class ServiceCollectionExtensions{
    /// <summary>
    /// Provider・HTTP(User-Agent・キャッシュ)・振り分け・IWeatherService を登録する。
    /// MAUI と Functions で同じ登録を使う(SolutionStructure.md 不変条件 6)。
    /// ILocationResolver と IHttpCacheStore は呼び出し側で登録する(未登録ならメモリストアを使う)。
    /// </summary>
    public static IServiceCollection AddWeatherProviders(this IServiceCollection services,Action<WeatherProviderOptions>? configure=null){
        var options=new WeatherProviderOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAppActivity>(AlwaysInUse.Instance);
        services.TryAddSingleton<IHttpCacheStore,MemoryHttpCacheStore>();
        services.TryAddSingleton<HttpCachePolicy>();
        services.TryAddSingleton<HttpCacheMaintenance>();
        services.AddTransient<UserAgentHandler>();
        services.AddTransient<HttpCacheHandler>();

        foreach(var name in new[]{JmaProvider.HttpClientName,NwsProvider.HttpClientName,MetNorwayProvider.HttpClientName}){
            var builder=services.AddHttpClient(name,client=>{
                //タイムアウトは HttpCacheHandler で扱う(古いキャッシュへの切り替えのため)
                client.Timeout=options.Timeout+TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<UserAgentHandler>()
            .AddHttpMessageHandler<HttpCacheHandler>();
            if(OperatingSystem.IsWindows()||OperatingSystem.IsLinux()||OperatingSystem.IsMacOS()){
                //デスクトップ(テスト・ツール・サーバー)のみ。モバイルはネイティブハンドラが自動で展開する
                builder.ConfigurePrimaryHttpMessageHandler(static ()=>new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All});
            }
        }

        services.TryAddSingleton(static sp=>new JmaDefinitions(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<JmaDefinitions>>()));
        services.TryAddSingleton(static sp=>new JmaProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<JmaDefinitions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<JmaProvider>>()));
        services.TryAddSingleton<NwsProvider>();
        services.TryAddSingleton<MetNorwayProvider>();

        services.AddSingleton<IForecastProvider>(static sp=>sp.GetRequiredService<JmaProvider>());
        services.AddSingleton<IForecastProvider>(static sp=>sp.GetRequiredService<NwsProvider>());
        services.AddSingleton<IForecastProvider>(static sp=>sp.GetRequiredService<MetNorwayProvider>());
        services.AddSingleton<IAlertProvider>(static sp=>sp.GetRequiredService<JmaProvider>());
        services.AddSingleton<IAlertProvider>(static sp=>sp.GetRequiredService<NwsProvider>());
        services.AddSingleton<IObservationProvider>(static sp=>sp.GetRequiredService<JmaProvider>());
        services.AddSingleton<IObservationProvider>(static sp=>sp.GetRequiredService<NwsProvider>());

        services.TryAddSingleton<WeatherProviderRouter>();
        services.TryAddSingleton<IWeatherService,WeatherService>();
        return services;
    }
}

/// <summary>HTTP キャッシュの容量管理(起動時に呼ぶ)。</summary>
public sealed class HttpCacheMaintenance(IHttpCacheStore store,WeatherProviderOptions options,TimeProvider time){
    public ValueTask TrimAsync(CancellationToken cancellationToken){
        return store.TrimAsync(options.CacheMaxBytes,options.CacheMaxUnused,time.GetUtcNow(),cancellationToken);
    }
}
