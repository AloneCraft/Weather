using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weather.Core;
using Weather.Presentation.ViewModels;

namespace Weather.Presentation;

public static class PresentationServiceCollectionExtensions{
    /// <summary>ViewModel と画面間の共有状態を登録する。抽象(INavigator 等)は App が登録する。</summary>
    public static IServiceCollection AddWeatherPresentation(this IServiceCollection services){
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<WeatherSession>();
        services.TryAddSingleton<AppSettings>();
        services.TryAddSingleton<FavoritesService>();
        services.TryAddSingleton<Func<PlaceData,PlaceWeatherViewModel>>(static sp=>data=>new PlaceWeatherViewModel(
            data,
            sp.GetRequiredService<IWeatherService>(),
            sp.GetRequiredService<WeatherSession>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<INavigator>(),
            sp.GetRequiredService<IAppLifecycle>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<MainViewModel>();
        services.TryAddTransient<HourlyViewModel>();
        services.TryAddTransient<DailyDetailViewModel>();
        services.TryAddTransient<AlertsViewModel>();
        services.TryAddTransient<AlertDetailViewModel>();
        services.TryAddTransient<SourcesViewModel>();
        services.TryAddTransient<SearchViewModel>();
        services.TryAddTransient<PlacesViewModel>();
        services.TryAddTransient<MapPickerViewModel>();
        services.TryAddTransient<SettingsViewModel>();
        services.TryAddTransient<AboutViewModel>();
        services.TryAddTransient<HistoryViewModel>();
        return services;
    }
}
