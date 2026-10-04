using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Weather.App.Services;
using Weather.Core;
using Weather.Presentation.Background;
using Weather.Presentation.Monetization;
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
                handlers.AddHandler<Controls.MetalCanvasView,Controls.MetalCanvasViewHandler>();
#if WEATHER_NATIVE_IOS
                handlers.AddHandler<Controls.NativeBanner,Monetization.NativeBannerHandler>();
#endif
#elif ANDROID
                handlers.AddHandler<Controls.NativeBanner,Monetization.NativeBannerHandler>();
#endif
            });

        var services=builder.Services;
        //MET の規約(使用中以外は取得しない)の判定に前面/背景を使う。AddWeatherProviders の既定(常に使用中)より先に登録する
        services.AddSingleton<AppLifecycle>();
        services.AddSingleton<IAppLifecycle>(static sp=>sp.GetRequiredService<AppLifecycle>());
        services.AddSingleton<IAppActivity>(static sp=>sp.GetRequiredService<AppLifecycle>());
        services.AddWeatherProviders(static options=>{
            //User-Agent の連絡先は GitHub リポジトリの URL(WeatherProviders.md)。リポジトリ公開時に確定する
            options.UserAgent="WeatherApp/"+AppInfo.Current.VersionString+" github.com/AloneCraft/Weather";
        });
        services.AddWeatherGeo();
        services.AddWeatherInfrastructure(static options=>{
            options.CacheDirectory=FileSystem.Current.CacheDirectory;
            options.DataDirectory=FileSystem.Current.AppDataDirectory;
        });
        //通知・ウィジェット・背景更新(AddWeatherPresentation の既定 = 何もしない実装より先に登録する)
#if ANDROID
        services.AddSingleton<Background.AndroidBackgroundPlatform>();
        services.AddSingleton<IAlertNotifier>(static sp=>sp.GetRequiredService<Background.AndroidBackgroundPlatform>());
        services.AddSingleton<IWidgetPublisher>(static sp=>sp.GetRequiredService<Background.AndroidBackgroundPlatform>());
        services.AddSingleton<IBackgroundScheduler>(static sp=>sp.GetRequiredService<Background.AndroidBackgroundPlatform>());
        //広告・同意・課金(Monetization.md)
        services.AddSingleton<IAdPlatform,Monetization.AndroidAdPlatform>();
        services.AddSingleton<IPurchasePlatform,Monetization.AndroidPurchasePlatform>();
#elif IOS
        services.AddSingleton<Background.IosBackgroundPlatform>();
        services.AddSingleton<IAlertNotifier>(static sp=>sp.GetRequiredService<Background.IosBackgroundPlatform>());
        services.AddSingleton<IWidgetPublisher>(static sp=>sp.GetRequiredService<Background.IosBackgroundPlatform>());
        services.AddSingleton<IBackgroundScheduler>(static sp=>sp.GetRequiredService<Background.IosBackgroundPlatform>());
#if WEATHER_NATIVE_IOS
        //広告・課金は Swift のラッパーを含むビルド(Mac で -p:EnableNativeIos=true)だけ。含まないビルドは何もしない実装のまま
        services.AddSingleton<IAdPlatform,Monetization.IosAdPlatform>();
        services.AddSingleton<IPurchasePlatform,Monetization.IosPurchasePlatform>();
#endif
#endif
        services.AddWeatherPresentation();

        services.AddSingleton<INavigator,MauiNavigator>();
        services.AddSingleton<IDialogService,MauiDialogService>();
        services.AddSingleton<ILocationService,MauiLocationService>();
        services.AddSingleton<ISettingsStore,PreferencesSettingsStore>();
        services.AddSingleton<IMotionPreferences,MotionPreferences>();

        services.AddSingleton<AppShell>();
        services.AddSingleton<MapPage>();
        services.AddTransient<PlaceDetailPage>();
        services.AddSingleton<IMapPixelSampler,MapPixelSampler>();
        services.AddTransient<HourlyPage>();
        services.AddTransient<DailyPage>();
        services.AddTransient<AlertsPage>();
        services.AddTransient<AlertDetailPage>();
        services.AddTransient<SourcesPage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<PlacesPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
