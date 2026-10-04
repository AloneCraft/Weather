using System.Globalization;
using Weather.Core;
using Weather.Remote.Contracts;

namespace Weather.Remote.Server;

/// <summary>応答(状態コード・JSON・キャッシュしてよい期間)。</summary>
public sealed record ApiResult(int Status,string Json,TimeSpan? MaxAge);

/// <summary>
/// サーバー側の要求処理。Functions のホストに依存しない(Weather.Functions は HTTP を受けてここへ渡すだけ)ため、単体テストできる。
/// 経路: resolve / forecast / alerts / stations / observations。座標はサーバー側でも小数 2 桁に丸める(キャッシュの粒度・プライバシー)。
/// </summary>
public sealed class WeatherApi(IWeatherService weather){
    public const string ResolveRoute="resolve";
    public const string ForecastRoute="forecast";
    public const string AlertsRoute="alerts";
    public const string StationsRoute="stations";
    public const string ObservationsRoute="observations";

    private static readonly TimeSpan ResolveMaxAge=TimeSpan.FromDays(1);
    private static readonly TimeSpan ForecastMaxAge=TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AlertsMaxAge=TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ObservationsMaxAge=TimeSpan.FromMinutes(5);

    /// <param name="query">クエリ文字列の値(なければ null)。</param>
    public async Task<ApiResult> HandleAsync(string route,Func<string,string?> query,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(query);
        try{
            switch(route){
                case ResolveRoute:{
                    var location=await weather.ResolveAsync(Point(query),cancellationToken).ConfigureAwait(false);
                    return Ok(this.Resolve(location),ResolveMaxAge);
                }
                case ForecastRoute:{
                    var location=await weather.ResolveAsync(Point(query),cancellationToken).ConfigureAwait(false);
                    var result=await weather.GetForecastAsync(location,cancellationToken).ConfigureAwait(false);
                    return Ok(ContractMapper.ToDto(result),ForecastMaxAge);
                }
                case AlertsRoute:{
                    var location=await weather.ResolveAsync(Point(query),cancellationToken).ConfigureAwait(false);
                    var result=await weather.GetAlertsAsync(location,cancellationToken).ConfigureAwait(false);
                    return Ok(ContractMapper.ToDto(result),AlertsMaxAge);
                }
                case StationsRoute:{
                    var location=await weather.ResolveAsync(Point(query),cancellationToken).ConfigureAwait(false);
                    var max=Math.Clamp(Integer(query,"max",5),1,20);
                    var stations=await weather.FindStationsAsync(location,max,cancellationToken).ConfigureAwait(false);
                    return Ok(new StationsResponse([..stations.Select(ContractMapper.ToDto)]),ResolveMaxAge);
                }
                case ObservationsRoute:{
                    var station=Station(query);
                    var from=Time(query,"from");
                    var to=Time(query,"to");
                    if(to<from){
                        throw new FormatException("to は from 以降である必要があります。");
                    }
                    var series=await weather.GetObservationsAsync(station,from,to,cancellationToken).ConfigureAwait(false);
                    return Ok(ContractMapper.ToDto(series),ObservationsMaxAge);
                }
                default:
                    return Error(404,new ErrorResponse(null,null,$"不明な経路です: {route}"));
            }
        }catch(WeatherProviderException ex){
            //Provider の失敗は 502。クライアントは同じ ProviderFailure の例外に戻す
            return Error(502,new ErrorResponse(ex.Provider,ex.Failure,ex.Message));
        }catch(Exception ex) when(ex is FormatException or ArgumentException or OverflowException){
            return Error(400,new ErrorResponse(null,null,ex.Message));
        }
    }

    private ResolveResponse Resolve(ResolvedLocation location){
        RetentionDto[] retention=[..Enum.GetValues<ProviderId>().Select(p=>new RetentionDto(p,weather.GetObservationServerRetention(p).TotalDays))];
        return new ResolveResponse(ContractMapper.ToDto(location),weather.GetObservationAvailability(location),weather.AllowsBackgroundFetch(location),retention);
    }

    private static GeoPoint Point(Func<string,string?> query){
        return new GeoPoint(Number(query,"lat"),Number(query,"lon")).RoundForRequest();
    }

    private static ObservationStation Station(Func<string,string?> query){
        var id=Required(query,"station");
        if(!Enum.TryParse<ProviderId>(Required(query,"provider"),true,out var provider)){
            throw new FormatException("provider が不正です。");
        }
        var name=query("name");
        if(string.IsNullOrWhiteSpace(name)){
            name=id;
        }
        return new ObservationStation(id,provider,name,new GeoPoint(Number(query,"lat"),Number(query,"lon")),null);
    }

    private static string Required(Func<string,string?> query,string name){
        var value=query(name);
        if(string.IsNullOrWhiteSpace(value)){
            throw new FormatException($"{name} がありません。");
        }
        return value;
    }

    private static double Number(Func<string,string?> query,string name){
        return double.Parse(Required(query,name),NumberStyles.Float,CultureInfo.InvariantCulture);
    }

    private static int Integer(Func<string,string?> query,string name,int defaultValue){
        var value=query(name);
        if(string.IsNullOrWhiteSpace(value)){
            return defaultValue;
        }
        return int.Parse(value,NumberStyles.Integer,CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset Time(Func<string,string?> query,string name){
        return DateTimeOffset.Parse(Required(query,name),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
    }

    private static ApiResult Ok<T>(T body,TimeSpan maxAge){
        return new ApiResult(200,RemoteJson.Serialize(body),maxAge);
    }

    private static ApiResult Error(int status,ErrorResponse body){
        return new ApiResult(status,RemoteJson.Serialize(body),null);
    }
}

