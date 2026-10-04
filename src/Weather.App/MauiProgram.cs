using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Weather.App.Services;
using Weather.App.Views;
using Weather.Geo;
using Weather.Infrastructure;
using Weather.Presentation;
using Weather.Providers;

namespace Weather.App;

/// <summary>DI の合成ルート。各ライブラリの AddXxx を使い、MAUI 固有の実装をここで登録する。</summary>
public static class MauiProgram{
    public static MauiApp CreateMauiApp(){
        var builder=MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(static fonts=>{
                fonts.AddFont("OpenSans-Regular.ttf","OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf","OpenSansSemibold");
            })
            .ConfigureMauiHandlers(static handlers=>{
#if IOS
                handlers.AddHandler<Controls.SceneMetalView,Controls.SceneMetalViewHandler>();
#endif
            });

        var services=builder.Services;
        services.AddWeatherProviders(static options=>{
            //User-Agent の連絡先は GitHub リポジトリの URL(WeatherProviders.md)。リポジトリ公開時に確定する
            options.UserAgent="WeatherApp/"+AppInfo.Current.VersionString+" github.com/AloneCraft/Weather";
        });
        services.AddWeatherGeo();
        services.AddWeatherInfrastructure(static options=>{
            options.CacheDirectory=FileSystem.Current.CacheDirectory;
            options.DataDirectory=FileSystem.Current.AppDataDirectory;
        });
        services.AddWeatherPresentation();

        services.AddSingleton<AppLifecycle>();
        services.AddSingleton<IAppLifecycle>(static sp=>sp.GetRequiredService<AppLifecycle>());
        services.AddSingleton<INavigator,MauiNavigator>();
        services.AddSingleton<IDialogService,MauiDialogService>();
        services.AddSingleton<ILocationService,MauiLocationService>();
        services.AddSingleton<ISettingsStore,PreferencesSettingsStore>();
        services.AddSingleton<IMotionPreferences,MotionPreferences>();

        services.AddSingleton<AppShell>();
        services.AddSingleton<MainPage>();
        services.AddTransient<HourlyPage>();
        services.AddTransient<DailyPage>();
        services.AddTransient<AlertsPage>();
        services.AddTransient<AlertDetailPage>();
        services.AddTransient<SourcesPage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<PlacesPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
