using System.Text.Json;
using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.Jma;

/// <summary>class20 から解決した気象庁の地域情報。</summary>
internal sealed record JmaArea(
    string Class20Code,
    string Class20Name,
    string Class10Code,
    string Class10Name,
    string OfficeCode,
    string OfficeName,
    string ForecastOfficeCode,
    string? TemperatureStationCode);

internal sealed record AmedasStation(string Code,string Name,double Latitude,double Longitude,double? AltitudeM,bool HasTemperature);

/// <summary>気象庁の定義データ(area.json / forecast_area.json / amedastable.json)。</summary>
internal sealed class JmaCatalog{
    private readonly Dictionary<string,(string Name,string Parent)> class20s=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(string Name,string Parent)> class15s=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(string Name,string Parent)> class10s=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> officeNames=new(StringComparer.Ordinal);
    private readonly Dictionary<string,(string ForecastOffice,string? Amedas)> class10Forecast=new(StringComparer.Ordinal);
    private readonly List<AmedasStation> stations=[];

    public IReadOnlyList<AmedasStation> Stations=>this.stations;

    public static JmaCatalog Parse(JsonElement area,JsonElement forecastArea,JsonElement amedasTable){
        var catalog=new JmaCatalog();
        ReadNodes(area.Required("class20s"),catalog.class20s);
        ReadNodes(area.Required("class15s"),catalog.class15s);
        ReadNodes(area.Required("class10s"),catalog.class10s);
        foreach(var office in area.Required("offices").EnumerateObject()){
            catalog.officeNames[office.Name]=office.Value.Str("name")??office.Name;
        }
        foreach(var office in forecastArea.EnumerateObject()){
            foreach(var item in office.Value.EnumerateArray()){
                var class10=item.Str("class10");
                if(class10 is null){
                    continue;
                }
                string? amedas=null;
                foreach(var code in item.Items("amedas")){
                    amedas=code.AsString();
                    break;
                }
                catalog.class10Forecast[class10]=(office.Name,amedas);
            }
        }
        foreach(var station in amedasTable.EnumerateObject()){
            var lat=ReadDegreesMinutes(station.Value.Required("lat"));
            var lon=ReadDegreesMinutes(station.Value.Required("lon"));
            var elems=station.Value.Str("elems")??"";
            catalog.stations.Add(new AmedasStation(
                station.Name,
                station.Value.Str("kjName")??station.Name,
                lat,
                lon,
                station.Value.Num("alt"),
                //elems の先頭桁を気温の有無とみなす(ビットの意味は要確認、WeatherProviders.md)
                elems.Length>0&&elems[0]=='1'));
        }
        return catalog;
    }

    public JmaArea? Resolve(string class20Code){
        if(!this.class20s.TryGetValue(class20Code,out var c20)){
            return null;
        }
        if(!this.class15s.TryGetValue(c20.Parent,out var c15)){
            return null;
        }
        if(!this.class10s.TryGetValue(c15.Parent,out var c10)){
            return null;
        }
        var office=c10.Parent;
        var officeName=office;
        if(this.officeNames.TryGetValue(office,out var name)){
            officeName=name;
        }
        var forecastOffice=office;
        string? station=null;
        if(this.class10Forecast.TryGetValue(c15.Parent,out var fc)){
            forecastOffice=fc.ForecastOffice;
            station=fc.Amedas;
        }
        return new JmaArea(class20Code,c20.Name,c15.Parent,c10.Name,office,officeName,forecastOffice,station);
    }

    public AmedasStation? FindStation(string code){
        foreach(var station in this.stations){
            if(station.Code==code){
                return station;
            }
        }
        return null;
    }

    private static void ReadNodes(JsonElement element,Dictionary<string,(string Name,string Parent)> target){
        foreach(var node in element.EnumerateObject()){
            target[node.Name]=(node.Value.Str("name")??node.Name,node.Value.Str("parent")??"");
        }
    }

    private static double ReadDegreesMinutes(JsonElement value){
        var parts=value.EnumerateArray().Select(static e=>e.AsDouble()??0).ToArray();
        return parts[0]+parts[1]/60;
    }
}

/// <summary>
/// 定義データの提供。同梱版を初期値とし、HTTP(キャッシュ層で 7 日に 1 回だけ再検証)で更新する。
/// </summary>
internal sealed partial class JmaDefinitions(IHttpClientFactory httpClientFactory,TimeProvider time,ILogger<JmaDefinitions> logger){
    private readonly Lock gate=new();
    private JmaCatalog? catalog;
    private Task? refresh;

    public JmaCatalog Current{
        get{
            lock(this.gate){
                if(this.catalog is null){
                    this.catalog=LoadEmbedded();
                }
                this.refresh??=Task.Run(this.RefreshAsync);
                return this.catalog;
            }
        }
    }

    public static JmaCatalog LoadEmbedded(){
        using var area=ReadEmbedded("area.json");
        using var forecastArea=ReadEmbedded("forecast_area.json");
        using var amedas=ReadEmbedded("amedastable.json");
        return JmaCatalog.Parse(area.RootElement,forecastArea.RootElement,amedas.RootElement);
    }

    private async Task RefreshAsync(){
        try{
            var client=httpClientFactory.CreateClient(JmaProvider.HttpClientName);
            var area=await ProviderHttp.GetAsync(client,ProviderId.Jma,JmaEndpoints.Area,time,CancellationToken.None).ConfigureAwait(false);
            var forecastArea=await ProviderHttp.GetAsync(client,ProviderId.Jma,JmaEndpoints.ForecastArea,time,CancellationToken.None).ConfigureAwait(false);
            var amedas=await ProviderHttp.GetAsync(client,ProviderId.Jma,JmaEndpoints.AmedasTable,time,CancellationToken.None).ConfigureAwait(false);
            using var areaDoc=JsonDocument.Parse(area.Body);
            using var forecastAreaDoc=JsonDocument.Parse(forecastArea.Body);
            using var amedasDoc=JsonDocument.Parse(amedas.Body);
            var updated=JmaCatalog.Parse(areaDoc.RootElement,forecastAreaDoc.RootElement,amedasDoc.RootElement);
            lock(this.gate){
                this.catalog=updated;
            }
        }catch(Exception ex) when(ex is WeatherProviderException or JsonException or KeyNotFoundException or InvalidOperationException){
            //同梱版で動作を続ける。失敗は記録する
            LogRefreshFailed(logger,ex);
        }
    }

    private static JsonDocument ReadEmbedded(string name){
        var stream=typeof(JmaDefinitions).Assembly.GetManifestResourceStream("Weather.Providers.Jma."+name)
            ??throw new InvalidOperationException($"同梱データがありません: {name}");
        using(stream){
            return JsonDocument.Parse(stream);
        }
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="気象庁の定義データを更新できませんでした。同梱版を使います。")]
    private static partial void LogRefreshFailed(ILogger logger,Exception exception);
}
