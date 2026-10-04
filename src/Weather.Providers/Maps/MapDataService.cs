using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Gfs;
using Weather.Providers.Http;
using Weather.Providers.Jma;

namespace Weather.Providers.Maps;

/// <summary>
/// 地図のデータ(Rendering.md「地図」)。日本周辺は気象庁、それ以外は GFS(方針 3 の追加・方針 5)。
/// 層と時刻ごとに、気象庁のどのプロダクトを使うかをここで決める:
/// - 降水: 雨雲の動き(解析・1 時間先まで)→ 今後の雨(15 時間先まで)→ 天気分布予報の 3 時間降水量
/// - 気温: 天気分布予報。現在時刻はアメダスの観測も重ねる
/// - 雲・気圧: 天気分布予報の天気
/// - 風: 現在時刻はアメダスの観測(矢印)、それ以降は海上分布予報(海域の風向の矢印)。
///   海上分布予報の風速のタイルは海域全体を薄い色で塗り地図が読めなくなるため使わない(強風は警報・注意報で伝わる)
/// 気象庁の予報期間を過ぎた日本周辺は OutOfRange にし、GFS で埋めない。
/// </summary>
internal sealed partial class MapDataService(GfsProvider gfs,JmaDefinitions definitions,IHttpClientFactory httpClientFactory,TimeProvider time,ILogger<MapDataService> logger):IMapDataService{
    public static readonly TimeSpan GfsTolerance=TimeSpan.FromMinutes(90);
    public static readonly TimeSpan NowcastTolerance=TimeSpan.FromMinutes(3);
    public static readonly TimeSpan RainForecastTolerance=TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DistributionTolerance=TimeSpan.FromMinutes(90);
    public static readonly TimeSpan MarineTolerance=TimeSpan.FromHours(3);
    /// <summary>アメダスは 10 分ごとの観測が数分遅れて出るため、時間軸の「現在」と最大 20 分ほどずれる。</summary>
    public static readonly TimeSpan AmedasTolerance=TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SnapshotLifetime=TimeSpan.FromMinutes(2);
    private const int FieldCacheSize=16;

    private readonly Lock gate=new();
    private readonly LinkedList<((GfsElement Element,DateTimeOffset Cycle,int Hour) Key,GridField Field)> fields=new();
    private Snapshot? snapshot;

    private sealed record Snapshot(DateTimeOffset FetchedAt,JmaTileTimes Tiles,DateTimeOffset? GfsCycle,IReadOnlyList<DateTimeOffset> GfsTimes,DateTimeOffset? AmedasTime,DateTimeOffset RetrievedAt);

    public async ValueTask<MapAvailability> GetAvailabilityAsync(CancellationToken cancellationToken){
        var s=await this.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return new MapAvailability{
            RetrievedAt=s.RetrievedAt,
            Nowcast=[..s.Tiles.Nowcast.Select(static t=>t.ValidTime)],
            RainForecast=[..s.Tiles.RainForecast.Select(static t=>t.ValidTime)],
            Distribution=[..s.Tiles.Distribution.Select(static t=>t.ValidTime)],
            Marine=[..s.Tiles.Marine.Select(static t=>t.ValidTime)],
            Gfs=s.GfsTimes,
            GfsReferenceTime=s.GfsCycle,
            AmedasTime=s.AmedasTime,
        };
    }

    public async ValueTask<MapFrame> GetFrameAsync(FieldLayer layer,DateTimeOffset at,CancellationToken cancellationToken){
        var s=await this.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var issues=new List<string>();
        var gfsTask=this.GetGfsAsync(layer,s,at,issues,cancellationToken);
        var japanTask=this.GetJapanAsync(layer,s,at,issues,cancellationToken);
        var (scalar,wind,pressure)=await gfsTask.ConfigureAwait(false);
        var japan=await japanTask.ConfigureAwait(false);
        var sources=new List<SourceAttribution>();
        foreach(var field in new[]{scalar,pressure,wind?.U}){
            if(field is not null&&!sources.Any(x=>x.Provider==ProviderId.Gfs)){
                sources.Add(field.Source);
            }
        }
        foreach(var tile in japan.Tiles){
            sources.Add(tile.Source);
        }
        if(japan.Arrows is not null){
            sources.Add(japan.Arrows.Source);
        }
        if(japan.Points is not null){
            sources.Add(japan.Points.Source);
        }
        return new MapFrame{
            Layer=layer,
            Time=at,
            Scalar=scalar,
            Wind=wind,
            Pressure=pressure,
            Tiles=japan.Tiles,
            Arrows=japan.Arrows,
            Points=japan.Points,
            Japan=japan.Coverage,
            Legend=LegendFor(layer),
            Sources=[..sources.DistinctBy(static x=>(x.Provider,x.ProductName,x.IssuedAt))],
            Issues=[..issues.Distinct(StringComparer.Ordinal)],
        };
    }

    public async ValueTask<byte[]?> GetTileAsync(JmaTileLayer layer,int z,int x,int y,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(layer);
        var client=httpClientFactory.CreateClient(JmaProvider.HttpClientName);
        try{
            var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,layer.TileUri(z,x,y),time,cancellationToken).ConfigureAwait(false);
            return fetch.Body;
        }catch(WeatherProviderException ex) when(JmaMaps.IsNotFound(ex)){
            return null;
        }
    }

    public static Legend LegendFor(FieldLayer layer){
        switch(layer){
            case FieldLayer.Wind:
                return MapLegends.WindSpeed;
            case FieldLayer.Precipitation:
                return MapLegends.RainRate;
            case FieldLayer.Temperature:
                return MapLegends.Temperature;
            default:
                return MapLegends.CloudCover;
        }
    }

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken cancellationToken){
        var now=time.GetUtcNow();
        lock(this.gate){
            if(this.snapshot is {} cached&&now-cached.FetchedAt<SnapshotLifetime){
                return cached;
            }
        }
        var client=httpClientFactory.CreateClient(JmaProvider.HttpClientName);
        var n1=this.TryTimesAsync(client,"nowc/targetTimes_N1.json",cancellationToken);
        var n2=this.TryTimesAsync(client,"nowc/targetTimes_N2.json",cancellationToken);
        var rasrf=this.TryTimesAsync(client,"rasrf/targetTimes.json",cancellationToken);
        var wdist=this.TryTimesAsync(client,"wdist/targetTimes.json",cancellationToken);
        var umimesh=this.TryTimesAsync(client,"umimesh/targetTimes.json",cancellationToken);
        var amedas=this.TryAmedasTimeAsync(client,cancellationToken);
        var cycle=this.TryCycleAsync(cancellationToken);
        var nowcast=JmaMaps.LatestPerValidTime((await n1.ConfigureAwait(false)).Concat(await n2.ConfigureAwait(false)));
        var rain=JmaMaps.LatestPerValidTime(await rasrf.ConfigureAwait(false));
        //天気分布予報・海上分布予報は最新の初期時刻の一式だけを使う(初期時刻の違う予報を混ぜない)
        var distribution=LatestBase(await wdist.ConfigureAwait(false));
        var marine=LatestBase(await umimesh.ConfigureAwait(false));
        var c=await cycle.ConfigureAwait(false);
        IReadOnlyList<DateTimeOffset> gfsTimes=[];
        if(c is {} value){
            gfsTimes=gfs.ValidTimes(value);
        }
        var result=new Snapshot(now,new JmaTileTimes(nowcast,rain,distribution,marine),c,gfsTimes,await amedas.ConfigureAwait(false),now);
        lock(this.gate){
            this.snapshot=result;
        }
        return result;
    }

    private static IReadOnlyList<JmaTargetTime> LatestBase(IReadOnlyList<JmaTargetTime> times){
        if(times.Count==0){
            return times;
        }
        var latest=times.Max(static t=>t.BaseTime);
        return [..times.Where(t=>t.BaseTime==latest).OrderBy(static t=>t.ValidTime)];
    }

    private async Task<IReadOnlyList<JmaTargetTime>> TryTimesAsync(HttpClient client,string path,CancellationToken cancellationToken){
        var uri=new Uri(JmaMaps.Root+path);
        try{
            var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,uri,time,cancellationToken).ConfigureAwait(false);
            using var doc=ProviderHttp.ParseJson(ProviderId.Jma,fetch,uri);
            return ProviderHttp.Map(ProviderId.Jma,uri,()=>JmaMaps.ParseTargetTimes(doc.RootElement));
        }catch(WeatherProviderException ex){
            LogPartFailed(logger,path,ex.Failure);
            return [];
        }
    }

    private async Task<DateTimeOffset?> TryAmedasTimeAsync(HttpClient client,CancellationToken cancellationToken){
        try{
            var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,JmaEndpoints.AmedasLatestTime,time,cancellationToken).ConfigureAwait(false);
            var text=System.Text.Encoding.UTF8.GetString(fetch.Body).Trim();
            if(DateTimeOffset.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var parsed)){
                return parsed;
            }
            return null;
        }catch(WeatherProviderException ex){
            LogPartFailed(logger,"amedas",ex.Failure);
            return null;
        }
    }

    private async Task<DateTimeOffset?> TryCycleAsync(CancellationToken cancellationToken){
        try{
            return await gfs.GetLatestCycleAsync(cancellationToken).ConfigureAwait(false);
        }catch(WeatherProviderException ex){
            LogPartFailed(logger,"gfs",ex.Failure);
            return null;
        }
    }

    private async Task<(GridField? Scalar,WindField? Wind,GridField? Pressure)> GetGfsAsync(FieldLayer layer,Snapshot s,DateTimeOffset at,List<string> issues,CancellationToken cancellationToken){
        if(s.GfsCycle is not {} cycle){
            if(s.GfsTimes.Count==0){
                lock(issues){
                    issues.Add("GFS");
                }
            }
            return (null,null,null);
        }
        var valid=Nearest(s.GfsTimes,at,GfsTolerance);
        if(valid is null){
            return (null,null,null);
        }
        var hour=(int)Math.Round((valid.Value-cycle).TotalHours);
        try{
            switch(layer){
                case FieldLayer.Wind:{
                    var u=this.GetFieldAsync(GfsElement.WindU,cycle,hour,cancellationToken);
                    var v=this.GetFieldAsync(GfsElement.WindV,cycle,hour,cancellationToken);
                    var wind=new WindField(await u.ConfigureAwait(false),await v.ConfigureAwait(false));
                    return (Speed(wind),wind,null);
                }
                case FieldLayer.Precipitation:
                    return (await this.GetFieldAsync(GfsElement.PrecipitationRate,cycle,hour,cancellationToken).ConfigureAwait(false),null,null);
                case FieldLayer.Temperature:
                    return (await this.GetFieldAsync(GfsElement.Temperature,cycle,hour,cancellationToken).ConfigureAwait(false),null,null);
                default:{
                    var clouds=this.GetFieldAsync(GfsElement.CloudCover,cycle,hour,cancellationToken);
                    var pressure=this.GetFieldAsync(GfsElement.Pressure,cycle,hour,cancellationToken);
                    return (await clouds.ConfigureAwait(false),null,await pressure.ConfigureAwait(false));
                }
            }
        }catch(WeatherProviderException ex){
            LogPartFailed(logger,"gfs",ex.Failure);
            lock(issues){
                issues.Add("GFS");
            }
            return (null,null,null);
        }
    }

    /// <summary>復号済みの格子を少数だけ覚えておく(時間軸を行き来するときに取り直さない)。</summary>
    private async Task<GridField> GetFieldAsync(GfsElement element,DateTimeOffset cycle,int hour,CancellationToken cancellationToken){
        var key=(element,cycle,hour);
        lock(this.gate){
            for(var node=this.fields.First;node is not null;node=node.Next){
                if(node.Value.Key==key){
                    this.fields.Remove(node);
                    this.fields.AddFirst(node);
                    return node.Value.Field;
                }
            }
        }
        var field=await gfs.GetFieldAsync(element,cycle,hour,cancellationToken).ConfigureAwait(false);
        lock(this.gate){
            this.fields.AddFirst((key,field));
            while(this.fields.Count>FieldCacheSize){
                this.fields.RemoveLast();
            }
        }
        return field;
    }

    /// <summary>風速の格子(色で塗る値)。日本周辺の NaN はそのまま伝わる。</summary>
    public static GridField Speed(WindField wind){
        ArgumentNullException.ThrowIfNull(wind);
        var u=wind.U.Values;
        var v=wind.V.Values;
        var speed=new float[u.Length];
        for(var i=0;i<u.Length;i++){
            speed[i]=MathF.Sqrt(u[i]*u[i]+v[i]*v[i]);
        }
        return new GridField(wind.U.Geometry,speed,FieldQuantity.WindSpeed,wind.U.ValidTime,wind.U.ReferenceTime,wind.U.Source);
    }

    private sealed record JapanPart(JapanCoverage Coverage,IReadOnlyList<JmaTileLayer> Tiles,WindArrowSet? Arrows,PointValueSet? Points);

    private async Task<JapanPart> GetJapanAsync(FieldLayer layer,Snapshot s,DateTimeOffset at,List<string> issues,CancellationToken cancellationToken){
        var t=s.Tiles;
        switch(layer){
            case FieldLayer.Precipitation:{
                if(JmaMaps.Nearest(t.Nowcast,at,NowcastTolerance) is {} n){
                    return new JapanPart(JapanCoverage.Available,[Tile(JmaTileProduct.Nowcast,"hrpns",n,MapLegends.RainRate,"降水ナウキャスト",JmaMaps.NowcastPage,s)],null,null);
                }
                if(JmaMaps.Nearest(t.RainForecast,at,RainForecastTolerance) is {} r){
                    return new JapanPart(JapanCoverage.Available,[Tile(JmaTileProduct.RainForecast,"rasrf",r,MapLegends.RainRate,"今後の雨(降水短時間予報)",JmaMaps.RainForecastPage,s)],null,null);
                }
                if(JmaMaps.Nearest(t.Distribution,at,DistributionTolerance,"r3") is {} d){
                    return new JapanPart(JapanCoverage.Available,[Tile(JmaTileProduct.DistributionForecast,"r3",d,MapLegends.Rain3h,"天気分布予報(降水量)",JmaMaps.DistributionPage,s)],null,null);
                }
                return new JapanPart(Missing(at,t.Nowcast,t.RainForecast,t.Distribution),[],null,null);
            }
            case FieldLayer.Temperature:{
                var tiles=new List<JmaTileLayer>();
                if(JmaMaps.Nearest(t.Distribution,at,DistributionTolerance,"temp") is {} d){
                    tiles.Add(Tile(JmaTileProduct.DistributionForecast,"temp",d,MapLegends.Temperature,"天気分布予報(気温)",JmaMaps.DistributionPage,s));
                }
                PointValueSet? points=null;
                if(s.AmedasTime is {} amedas&&(amedas-at).Duration()<=AmedasTolerance){
                    var observed=await this.TryAmedasAsync(amedas,issues,cancellationToken).ConfigureAwait(false);
                    if(observed is {} o){
                        points=new PointValueSet(o.Temperature,FieldQuantity.TemperatureC,amedas,o.Source);
                    }
                }
                if(tiles.Count==0&&points is null){
                    return new JapanPart(Missing(at,t.Distribution),[],null,null);
                }
                return new JapanPart(JapanCoverage.Available,tiles,null,points);
            }
            case FieldLayer.Wind:{
                if(s.AmedasTime is {} amedas&&(amedas-at).Duration()<=AmedasTolerance){
                    var observed=await this.TryAmedasAsync(amedas,issues,cancellationToken).ConfigureAwait(false);
                    if(observed is {} o){
                        return new JapanPart(JapanCoverage.Available,[],new WindArrowSet(o.Wind,ArrowKind.Observation,amedas,o.Source),null);
                    }
                }
                if(JmaMaps.Nearest(t.Marine,at,MarineTolerance) is {} m){
                    var arrows=await this.TryMarineWindAsync(m,s,issues,cancellationToken).ConfigureAwait(false);
                    if(arrows is not null){
                        return new JapanPart(JapanCoverage.Available,[],arrows,null);
                    }
                }
                return new JapanPart(Missing(at,t.Marine),[],null,null);
            }
            default:{
                if(JmaMaps.Nearest(t.Distribution,at,DistributionTolerance,"wm") is {} d){
                    return new JapanPart(JapanCoverage.Available,[Tile(JmaTileProduct.DistributionForecast,"wm",d,MapLegends.Weather,"天気分布予報(天気)",JmaMaps.DistributionPage,s)],null,null);
                }
                return new JapanPart(Missing(at,t.Distribution),[],null,null);
            }
        }
    }

    /// <summary>予報期間を過ぎていれば OutOfRange、それ以外(過去・該当プロダクトなし)は NotAvailable。</summary>
    private static JapanCoverage Missing(DateTimeOffset at,params IReadOnlyList<JmaTargetTime>[] products){
        var last=products.SelectMany(static p=>p).Select(static p=>(DateTimeOffset?)p.ValidTime).Max();
        if(last is {} l&&at>l){
            return JapanCoverage.OutOfRange;
        }
        return JapanCoverage.NotAvailable;
    }

    private static JmaTileLayer Tile(JmaTileProduct product,string element,JmaTargetTime t,Legend legend,string name,Uri page,Snapshot s){
        var marine=product==JmaTileProduct.MarineForecast;
        var maxZoom=10;
        if(marine){
            maxZoom=8;
        }
        return new JmaTileLayer{
            Product=product,
            Element=element,
            BaseTime=t.BaseTime,
            ValidTime=t.ValidTime,
            MinZoom=4,
            MaxZoom=maxZoom,
            EvenZoomOnly=!marine,
            Pixelated=!marine,
            Legend=legend,
            Source=JmaMaps.Source(name,t.BaseTime,s.RetrievedAt,false,page),
        };
    }

    private sealed record AmedasObservation(IReadOnlyList<WindArrow> Wind,IReadOnlyList<PointValue> Temperature,SourceAttribution Source);

    private async Task<AmedasObservation?> TryAmedasAsync(DateTimeOffset at,List<string> issues,CancellationToken cancellationToken){
        var client=httpClientFactory.CreateClient(JmaProvider.HttpClientName);
        var uri=JmaMaps.AmedasMapUri(at);
        try{
            var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,uri,time,cancellationToken).ConfigureAwait(false);
            using var doc=ProviderHttp.ParseJson(ProviderId.Jma,fetch,uri);
            var (wind,temperature)=ProviderHttp.Map(ProviderId.Jma,uri,()=>JmaMaps.ParseAmedasMap(doc.RootElement,definitions.Current));
            return new AmedasObservation(wind,temperature,JmaMaps.Source("アメダス",at,fetch.RetrievedAt,fetch.IsStale,JmaMaps.AmedasPage));
        }catch(WeatherProviderException ex){
            LogPartFailed(logger,"amedas",ex.Failure);
            lock(issues){
                issues.Add("アメダス");
            }
            return null;
        }
    }

    private async Task<WindArrowSet?> TryMarineWindAsync(JmaTargetTime t,Snapshot s,List<string> issues,CancellationToken cancellationToken){
        var client=httpClientFactory.CreateClient(JmaProvider.HttpClientName);
        var uri=new Uri(System.FormattableString.Invariant($"{JmaMaps.Root}umimesh/{t.BaseTime.UtcDateTime:yyyyMMddHHmmss}/none/{t.ValidTime.UtcDateTime:yyyyMMddHHmmss}/surf/wd/data.geojson"));
        try{
            var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,uri,time,cancellationToken).ConfigureAwait(false);
            var arrows=ProviderHttp.Map(ProviderId.Jma,uri,()=>JmaMaps.ParseMarineWind(fetch.Body));
            return new WindArrowSet(arrows,ArrowKind.Forecast,t.ValidTime,JmaMaps.Source("海上分布予報(風)",t.BaseTime,fetch.RetrievedAt,fetch.IsStale,JmaMaps.MarinePage));
        }catch(WeatherProviderException ex){
            LogPartFailed(logger,"umimesh",ex.Failure);
            lock(issues){
                issues.Add("海上分布予報");
            }
            return null;
        }
    }

    private static DateTimeOffset? Nearest(IReadOnlyList<DateTimeOffset> times,DateTimeOffset at,TimeSpan tolerance){
        DateTimeOffset? best=null;
        var bestDistance=TimeSpan.MaxValue;
        foreach(var t in times){
            var d=(t-at).Duration();
            if(d<=tolerance&&d<bestDistance){
                best=t;
                bestDistance=d;
            }
        }
        return best;
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="地図のデータの一部を取得できませんでした: {Part} {Failure}")]
    private static partial void LogPartFailed(ILogger logger,string part,ProviderFailure failure);
}
