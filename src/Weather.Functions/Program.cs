using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weather.Geo;
using Weather.Providers;
using Weather.Remote.Server;

//アプリと同じ登録(AddWeatherProviders / AddWeatherGeo)をサーバーで再利用する(SolutionStructure.md 不変条件 6)。
//HTTP キャッシュは Providers 既定のメモリ実装(インスタンスごと)。複数インスタンスで共有する場合は IHttpCacheStore を Blob 等に差し替える。
var builder=FunctionsApplication.CreateBuilder(args);
builder.Services.AddWeatherProviders(static options=>{
    //User-Agent の連絡先は公開リポジトリ(WeatherProviders.md)。サーバーからの取得であることを区別できる名前にする
    options.UserAgent="WeatherAppServer/0.1 github.com/AloneCraft/Weather";
});
builder.Services.AddWeatherGeo();
builder.Services.AddSingleton<WeatherApi>();
builder.Build().Run();
