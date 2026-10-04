using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weather.Core;
using Weather.Presentation.Background;
using Weather.Presentation.Monetization;
using Weather.Presentation.ViewModels;

namespace Weather.Presentation;

public static class PresentationServiceCollectionExtensions{
    /// <summary>ViewModel と画面間の共有状態を登録する。抽象(INavigator 等)は App が登録する。</summary>
    public static IServiceCollection AddWeatherPresentation(this IServiceCollection services){
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<WeatherSession>();
        services.TryAddSingleton<AppSettings>();
        services.TryAddSingleton<FavoritesService>();
        //通知・ウィジェット・背景更新(各 OS の実装は App が登録する。未登録なら何もしない)
        services.TryAddSingleton<NullBackgroundPlatform>();
        services.TryAddSingleton<IAlertNotifier>(static sp=>sp.GetRequiredService<NullBackgroundPlatform>());
        services.TryAddSingleton<IWidgetPublisher>(static sp=>sp.GetRequiredService<NullBackgroundPlatform>());
        services.TryAddSingleton<IBackgroundScheduler>(static sp=>sp.GetRequiredService<NullBackgroundPlatform>());
        //広告と課金(各 OS の実装は App が登録する。未登録なら広告を出さず購入もできない)
        services.TryAddSingleton<NullMonetizationPlatform>();
        services.TryAddSingleton<IAdPlatform>(static sp=>sp.GetRequiredService<NullMonetizationPlatform>());
        services.TryAddSingleton<IPurchasePlatform>(static sp=>sp.GetRequiredService<NullMonetizationPlatform>());
        services.TryAddSingleton<AdPolicy>();
        services.TryAddSingleton<WidgetUpdater>();
        services.TryAddSingleton<BackgroundRefreshService>();
        services.TryAddSingleton<Func<PlaceData,PlaceWeatherViewModel>>(static sp=>data=>new PlaceWeatherViewModel(
            data,
            sp.GetRequiredService<IWeatherService>(),
            sp.GetRequiredService<WeatherSession>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<IAppLifecycle>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<MainViewModel>();
        services.TryAddSingleton<MapFocusService>();
        services.TryAddSingleton<IMapPixelSampler,NullMapPixelSampler>();
        services.TryAddSingleton<MapViewModel>();
        services.TryAddTransient<HourlyViewModel>();
        services.TryAddTransient<DailyDetailViewModel>();
        services.TryAddTransient<AlertsViewModel>();
        services.TryAddTransient<AlertDetailViewModel>();
        services.TryAddTransient<SourcesViewModel>();
        services.TryAddTransient<SearchViewModel>();
        services.TryAddTransient<PlacesViewModel>();
        services.TryAddTransient<SettingsViewModel>();
        services.TryAddSingleton<AdSettingsViewModel>();
        services.TryAddTransient<AboutViewModel>();
        services.TryAddTransient<HistoryViewModel>();
        return services;
    }
}
