using Weather.Core;
using Weather.Presentation;
using Weather.Rendering.Map;

namespace Weather.App.Services;

/// <summary>吹き出し用: 気象庁のタイルを取得し、地点の画素の色を読む(タイルは HTTP キャッシュに残る)。</summary>
public sealed class MapPixelSampler(IMapDataService maps):IMapPixelSampler{
    public async ValueTask<uint?> SampleAsync(JmaTileLayer layer,GeoPoint point,CancellationToken cancellationToken){
        var (z,x,y,px,py)=JmaTileSampler.Locate(layer,point);
        try{
            var png=await maps.GetTileAsync(layer,z,x,y,cancellationToken);
            if(png is null){
                return null;
            }
            return JmaTileSampler.Pixel(png,px,py);
        }catch(WeatherProviderException){
            return null;
        }
    }
}
