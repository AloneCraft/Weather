using System.Net;
using System.Text;
using Weather.Core;
using Weather.Remote.Contracts;
using Weather.Remote.Server;

namespace Weather.Remote.Tests;

internal static class Sample{
    public static readonly DateTimeOffset Now=new(2026,10,4,3,0,0,TimeSpan.Zero);
    public static readonly ObservationStation Station=new("44132",ProviderId.Jma,"東京",new GeoPoint(35.69,139.75),25);

    public static SourceAttribution Source(ProviderId provider,string product="forecast",DataProcessing processing=DataProcessing.None){
        var agency="MET Norway";
        if(provider==ProviderId.Jma){
            agency="気象庁";
        }
        return new SourceAttribution{
            Provider=provider,
            AgencyName=agency,
            ProductName=product,
            PublishingOffice="気象庁",
            IssuedAt=Now.AddHours(-2),
            RetrievedAt=Now,
            License=new LicenseInfo("公共データ利用規約",new Uri("https://www.jma.go.jp/jma/kishou/info/coment.html")),
            SourceUrl=new Uri("https://www.jma.go.jp/bosai/forecast/"),
            Processing=processing,
        };
    }

    public static ResolvedLocation Tokyo=>new(){Point=new GeoPoint(35.69,139.75),CountryCode="JP",DisplayName="千代田区",TimeZoneId="Asia/Tokyo",AdminName="東京都",JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Inside,"1310100",0)};
    public static ResolvedLocation Oslo=>new(){Point=new GeoPoint(59.91,10.75),CountryCode="NO",DisplayName="Oslo",TimeZoneId="Europe/Oslo"};

    /// <summary>電文の全項目を使う気象庁の予報(複合天気・幅・時間帯・気温の別出典・部分結果)。</summary>
    public static Forecast JmaForecast(){
        var src=Source(ProviderId.Jma);
        var temperature=Source(ProviderId.Jma,"weekly");
        var points=new List<ForecastPoint>();
        for(var i=0;i<8;i++){
            points.Add(new ForecastPoint(Now.AddHours(3*i),Now.AddHours(3*i+3),src){
                Condition=new CompositeCondition(new WeatherCondition(SkyCover.Overcast),ConditionTransition.Occasionally,new WeatherCondition(SkyCover.Overcast,PrecipitationType.Rain,PrecipitationIntensity.Light,isShowery:true)),
                SourceCode="212",
                WeatherText="くもり 時々 雨",
                TemperatureC=20.5+i,
                WindSpeedRangeMs=new ValueRange(3,5),
                WindDirectionDeg=45,
            });
        }
        var day=new DailyForecast(new DateOnly(2026,10,4),src){
            TemperatureSource=temperature,
            Condition=new WeatherCondition(SkyCover.Overcast),
            SourceCode="200",
            WeatherText="くもり",
            WindText="北の風",
            WaveText="1メートル",
            TempMaxC=24,
            TempMinC=17.5,
            TempMaxRangeC=new ValueRange(22,26),
            TemperaturePointName="東京",
            PrecipitationProbability=30,
            Parts=[new DayPart(Now,Now.AddHours(6)){Label="06-12",PrecipitationProbability=20},new DayPart(Now.AddHours(6),Now.AddHours(12)){Label="12-18",PrecipitationProbability=30}],
            Reliability=ForecastReliability.B,
        };
        return new Forecast(Tokyo,points,[day],"東京地方",[new DataIssue(ProviderId.Jma,"wdist",ProviderFailure.Timeout)]);
    }

    public static AlertSet JmaAlerts(){
        var src=Source(ProviderId.Jma,"warning");
        return new AlertSet([new Alert("1310100:43","レベル4大雨危険警報",AlertTier.Danger,AlertStatus.Active,src){EventCode="43",WarningLevel=4,AreaName="千代田区",Headline="大雨に警戒してください。"}],src,"大雨に警戒してください。");
    }

    public static ObservationSeries Observations(){
        var src=Source(ProviderId.Jma,"amedas");
        Observation[] items=[
            new(Now.AddMinutes(-10)){TemperatureC=new Measurement(20.1),WindDirectionDeg=new Measurement(90),Precipitation10mMm=new Measurement(0.5,MeasurementQuality.Suspect)},
            new(Now){TemperatureC=new Measurement(20.3),HumidityPercent=new Measurement(70)},
        ];
        return new ObservationSeries(Station,items,src,[new DailyObservationSummary(Station.Key,new DateOnly(2026,10,4)){MaxTempC=24.1,MaxTempAt=Now.AddHours(-1),MinTempC=16.2}]);
    }
}

/// <summary>サーバー側の IWeatherService の代わり(呼ばれた座標を記録する)。</summary>
internal sealed class FakeWeatherService:IWeatherService{
    public List<GeoPoint> ResolvedPoints{get;}=[];
    public WeatherProviderException? Error{get;set;}
    public ResolvedLocation Location{get;set;}=Sample.Tokyo;

    public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
        this.ResolvedPoints.Add(point);
        return ValueTask.FromResult(this.Location with{Point=point});
    }

    public ValueTask<ForecastResult> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        if(this.Error is not null){
            throw this.Error;
        }
        if(location.JmaArea is {IsCovered:false}){
            return ValueTask.FromResult(ForecastResult.OutOfCoverage);
        }
        return ValueTask.FromResult(new ForecastResult(Availability.Available,Sample.JmaForecast()));
    }

    public ValueTask<AlertResult> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
        return ValueTask.FromResult(new AlertResult(Availability.Available,Sample.JmaAlerts()));
    }

    public Availability GetObservationAvailability(ResolvedLocation location){
        return Availability.Available;
    }

    public ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
        IReadOnlyList<ObservationStation> list=[Sample.Station];
        return ValueTask.FromResult(list);
    }

    public ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        return ValueTask.FromResult(Sample.Observations());
    }

    public TimeSpan GetObservationServerRetention(ProviderId provider){
        if(provider==ProviderId.Jma){
            return TimeSpan.FromDays(10);
        }
        return TimeSpan.FromDays(7);
    }

    public bool AllowsBackgroundFetch(ResolvedLocation location){
        return location.IsJapanArea;
    }
}

/// <summary>HTTP を経由せずに WeatherApi へ渡す(Functions の WeatherFunctions と同じ変換)。</summary>
internal sealed class InProcessHandler(Server.WeatherApi api):HttpMessageHandler{
    public Exception? Failure{get;set;}
    public List<Uri> Requests{get;}=[];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        var uri=request.RequestUri!;
        this.Requests.Add(uri);
        if(this.Failure is not null){
            throw this.Failure;
        }
        var query=System.Web.HttpUtility.ParseQueryString(uri.Query);
        var result=await api.HandleAsync(uri.AbsolutePath.TrimStart('/').Replace("api/","",StringComparison.Ordinal),name=>query[name],cancellationToken);
        return new HttpResponseMessage((HttpStatusCode)result.Status){Content=new StringContent(result.Json,Encoding.UTF8,"application/json")};
    }
}

/// <summary>決まった JSON を 200 で返す(サーバーの誤った応答の再現用)。</summary>
internal sealed class FixedJsonHandler(string json):HttpMessageHandler{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
    }
}

internal sealed class RemoteHarness{
    public FakeWeatherService Server{get;}=new();
    public InProcessHandler Handler{get;}
    public Remote.RemoteWeatherService Client{get;}

    public RemoteHarness(){
        this.Handler=new InProcessHandler(new Server.WeatherApi(this.Server));
        this.Client=new Remote.RemoteWeatherService(new HttpClient(this.Handler){BaseAddress=new Uri("https://example.test/api/")});
    }
}

public class ContractMapper{
    [Fact,Trait("Category","Unit")]public void ToDto(){
        {
            //予報の往復: 電文 → JSON → 電文 → モデル → 電文が同じ(全項目が欠けずに戻る)
            var original=Sample.JmaForecast();
            var json=RemoteJson.Serialize(Contracts.ContractMapper.ToDto(new ForecastResult(Availability.Available,original)));
            var restored=Contracts.ContractMapper.ToModel(RemoteJson.Deserialize<ForecastResponse>(json));
            Assert.Equal(json,RemoteJson.Serialize(Contracts.ContractMapper.ToDto(restored)));
            Assert.Equal(original.TimeSeries,restored.Forecast!.TimeSeries);
            Assert.Equal(original.Location,restored.Forecast.Location);
            Assert.Equal(original.Issues,restored.Forecast.Issues);
            Assert.Equal(original.Daily[0].TemperatureSource,restored.Forecast.Daily[0].TemperatureSource);
            Assert.Equal(2,restored.Forecast.Daily[0].Parts.Count);
        }
        {
            //日本周辺で気象庁の区域から遠い地点(OutOfCoverage)の距離は無限大。直列化で失敗せず、往復で値が保たれる
            var location=Sample.Tokyo with{JmaArea=new JmaAreaMatch(JmaAreaMatchKind.OutOfCoverage,null,double.PositiveInfinity)};
            var json=RemoteJson.Serialize(new ResolveResponse(Contracts.ContractMapper.ToDto(location),Availability.NotSupported,false,[]));
            var restored=Contracts.ContractMapper.ToModel(RemoteJson.Deserialize<ResolveResponse>(json).Location);
            Assert.Equal(location,restored);
        }
        {
            //出典は表に 1 回だけ載せる(8 区間 + 日別で出典は 2 種類)
            var dto=Contracts.ContractMapper.ToDto(Sample.JmaForecast());
            Assert.Equal(2,dto.Sources.Count);
        }
        {
            //JSON は camelCase・列挙は文字列・null は省略
            var json=RemoteJson.Serialize(Contracts.ContractMapper.ToDto(new ForecastResult(Availability.Available,Sample.JmaForecast())));
            Assert.Contains("\"availability\":\"Available\"",json,StringComparison.Ordinal);
            Assert.Contains("\"provider\":\"Jma\"",json,StringComparison.Ordinal);
            Assert.DoesNotContain("\"cloudCoverPercent\"",json,StringComparison.Ordinal);
        }
    }

    [Fact,Trait("Category","Unit")]public void ToModel(){
        {
            //方針 5: 日本域に気象庁以外の出典を含む電文はモデルに戻せない(サーバーの誤りをクライアントで検出する)
            var dto=Contracts.ContractMapper.ToDto(Sample.JmaForecast());
            var met=Contracts.ContractMapper.ToDto(Sample.Source(ProviderId.MetNorway));
            var broken=dto with{Sources=[met,..dto.Sources.Skip(1)]};
            Assert.Throws<InvalidOperationException>(()=>Contracts.ContractMapper.ToModel(broken));
        }
        {
            //警報・観測の往復(名称・見出しは原文のまま、準正常値の品質も保つ)
            var alerts=Contracts.ContractMapper.ToModel(RemoteJson.Deserialize<AlertResponse>(RemoteJson.Serialize(Contracts.ContractMapper.ToDto(new AlertResult(Availability.Available,Sample.JmaAlerts())))));
            Assert.Equal(Sample.JmaAlerts().Alerts,alerts.Alerts!.Alerts);
            var series=Contracts.ContractMapper.ToModel(RemoteJson.Deserialize<ObservationSeriesDto>(RemoteJson.Serialize(Contracts.ContractMapper.ToDto(Sample.Observations()))),Sample.Station);
            Assert.Equal(Sample.Observations().Items,series.Items);
            Assert.Equal(Sample.Observations().DailySummaries,series.DailySummaries);
        }
    }
}

public class WeatherApi{
    private static Func<string,string?> Query(params (string Key,string Value)[] items){
        var values=items.ToDictionary(static i=>i.Key,static i=>i.Value,StringComparer.Ordinal);
        return name=>values.GetValueOrDefault(name);
    }

    [Fact,Trait("Category","Unit")]public async Task HandleAsync(){
        var ct=TestContext.Current.CancellationToken;
        {
            //座標はサーバー側でも小数 2 桁に丸め、キャッシュしてよい期間を返す
            var server=new FakeWeatherService();
            var api=new Server.WeatherApi(server);
            var result=await api.HandleAsync(Server.WeatherApi.ForecastRoute,Query(("lat","35.68123"),("lon","139.76789")),ct);
            Assert.Equal(200,result.Status);
            Assert.Equal(TimeSpan.FromMinutes(5),result.MaxAge);
            Assert.Equal(new GeoPoint(35.68,139.77),Assert.Single(server.ResolvedPoints));
        }
        {
            //Provider の失敗は 502 で、失敗の種類を返す
            var server=new FakeWeatherService{Error=new WeatherProviderException(ProviderId.Jma,ProviderFailure.RateLimited,"429")};
            var result=await new Server.WeatherApi(server).HandleAsync(Server.WeatherApi.ForecastRoute,Query(("lat","35.68"),("lon","139.76")),ct);
            Assert.Equal(502,result.Status);
            var error=RemoteJson.Deserialize<ErrorResponse>(result.Json);
            Assert.Equal(ProviderFailure.RateLimited,error.Failure);
            Assert.Equal(ProviderId.Jma,error.Provider);
            Assert.Null(result.MaxAge);
        }
        {
            //要求の誤りは 400、不明な経路は 404
            var api=new Server.WeatherApi(new FakeWeatherService());
            Assert.Equal(400,(await api.HandleAsync(Server.WeatherApi.ForecastRoute,Query(("lat","abc"),("lon","139")),ct)).Status);
            Assert.Equal(400,(await api.HandleAsync(Server.WeatherApi.ForecastRoute,Query(("lat","95"),("lon","139")),ct)).Status);
            Assert.Equal(400,(await api.HandleAsync(Server.WeatherApi.ForecastRoute,static _=>null,ct)).Status);
            Assert.Equal(404,(await api.HandleAsync("unknown",static _=>null,ct)).Status);
        }
    }
}

public class RemoteWeatherService{
    [Fact,Trait("Category","Unit")]public async Task ResolveAsync(){
        var h=new RemoteHarness();
        var ct=TestContext.Current.CancellationToken;
        {
            //resolve する前は同期の問い合わせに安全側で答える(観測なし・背景取得しない)
            Assert.Equal(Availability.NotSupported,h.Client.GetObservationAvailability(Sample.Tokyo));
            Assert.False(h.Client.AllowsBackgroundFetch(Sample.Tokyo));
            Assert.Equal(TimeSpan.Zero,h.Client.GetObservationServerRetention(ProviderId.Jma));
        }
        {
            //resolve の応答で同期の問い合わせに答える。送る座標は小数 2 桁
            var location=await h.Client.ResolveAsync(new GeoPoint(35.68123,139.76789),ct);
            Assert.Equal("千代田区",location.DisplayName);
            Assert.Equal(Availability.Available,h.Client.GetObservationAvailability(location));
            Assert.True(h.Client.AllowsBackgroundFetch(location));
            Assert.Equal(TimeSpan.FromDays(10),h.Client.GetObservationServerRetention(ProviderId.Jma));
            Assert.Contains("lat=35.68&lon=139.77",h.Handler.Requests[0].Query,StringComparison.Ordinal);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync(){
        var ct=TestContext.Current.CancellationToken;
        {
            //サーバーの予報がそのまま戻る
            var h=new RemoteHarness();
            var result=await h.Client.GetForecastAsync(Sample.Tokyo,ct);
            Assert.Equal(Availability.Available,result.Availability);
            Assert.Equal(Sample.JmaForecast().TimeSeries,result.Forecast!.TimeSeries);
        }
        {
            //サーバーでの Provider の失敗は同じ種類の WeatherProviderException になる
            var h=new RemoteHarness();
            h.Server.Error=new WeatherProviderException(ProviderId.Jma,ProviderFailure.ServerError,"500");
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await h.Client.GetForecastAsync(Sample.Tokyo,ct));
            Assert.Equal(ProviderFailure.ServerError,ex.Failure);
            Assert.Equal(ProviderId.Jma,ex.Provider);
        }
        {
            //方針 5: 日本域の予報に MET の出典を含む応答はクライアントで拒否し、WeatherProviderException(InvalidResponse)にする
            var dto=Contracts.ContractMapper.ToDto(new ForecastResult(Availability.Available,Sample.JmaForecast()));
            var met=Contracts.ContractMapper.ToDto(Sample.Source(ProviderId.MetNorway));
            var broken=dto with{Forecast=dto.Forecast! with{Sources=[met,..dto.Forecast.Sources.Skip(1)]}};
            var client=new Remote.RemoteWeatherService(new HttpClient(new FixedJsonHandler(RemoteJson.Serialize(broken))){BaseAddress=new Uri("https://example.test/api/")});
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await client.GetForecastAsync(Sample.Tokyo,ct));
            Assert.Equal(ProviderFailure.InvalidResponse,ex.Failure);
        }
        {
            //範囲外の座標を含む地点の解決・観測所の応答も InvalidResponse
            var station=new StationDto("44132",ProviderId.Jma,"東京",new GeoPointDto(95,139.75),25);
            var client=new Remote.RemoteWeatherService(new HttpClient(new FixedJsonHandler(RemoteJson.Serialize(new StationsResponse([station])))){BaseAddress=new Uri("https://example.test/api/")});
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await client.FindStationsAsync(Sample.Tokyo,3,ct));
            Assert.Equal(ProviderFailure.InvalidResponse,ex.Failure);
            var bad=new ResolveResponse(Contracts.ContractMapper.ToDto(Sample.Tokyo) with{Point=new GeoPointDto(95,139.75)},Availability.Available,true,[]);
            var resolver=new Remote.RemoteWeatherService(new HttpClient(new FixedJsonHandler(RemoteJson.Serialize(bad))){BaseAddress=new Uri("https://example.test/api/")});
            var ex2=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await resolver.ResolveAsync(new GeoPoint(35.69,139.75),ct));
            Assert.Equal(ProviderFailure.InvalidResponse,ex2.Failure);
        }
        {
            //サーバーに届かないときは Network / Timeout
            var h=new RemoteHarness();
            h.Handler.Failure=new HttpRequestException("down");
            Assert.Equal(ProviderFailure.Network,(await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await h.Client.GetForecastAsync(Sample.Oslo,ct))).Failure);
            h.Handler.Failure=new TaskCanceledException("timeout");
            Assert.Equal(ProviderFailure.Timeout,(await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await h.Client.GetForecastAsync(Sample.Oslo,ct))).Failure);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task InvalidResponses(){
        //サーバーの応答の JSON の 1 か所を壊す(固定シード)。どう壊れていても WeatherProviderException(InvalidResponse)以外の例外を漏らさない
        var ct=TestContext.Current.CancellationToken;
        var ops=new List<(string Name,string Json,Func<Remote.RemoteWeatherService,Task> Call)>{
            ("forecast",RemoteJson.Serialize(Contracts.ContractMapper.ToDto(new ForecastResult(Availability.Available,Sample.JmaForecast()))),async c=>await c.GetForecastAsync(Sample.Tokyo,ct)),
            ("alerts",RemoteJson.Serialize(Contracts.ContractMapper.ToDto(new AlertResult(Availability.Available,Sample.JmaAlerts()))),async c=>await c.GetAlertsAsync(Sample.Tokyo,ct)),
            ("resolve",RemoteJson.Serialize(new ResolveResponse(Contracts.ContractMapper.ToDto(Sample.Tokyo),Availability.Available,true,[new RetentionDto(ProviderId.Jma,10)])),async c=>await c.ResolveAsync(new GeoPoint(35.69,139.75),ct)),
            ("stations",RemoteJson.Serialize(new StationsResponse([new StationDto("44132",ProviderId.Jma,"東京",new GeoPointDto(35.69,139.75),25)])),async c=>await c.FindStationsAsync(Sample.Tokyo,3,ct)),
            ("observations",RemoteJson.Serialize(Contracts.ContractMapper.ToDto(Sample.Observations())),async c=>await c.GetObservationsAsync(Sample.Station,Sample.Now.AddDays(-1),Sample.Now,ct)),
        };
        foreach(var (name,json,call) in ops){
            for(var i=0;i<200;i++){
                var mutated=JsonMutator.Mutate(json,new Random(HashCode.Combine(name,i)));
                var client=new Remote.RemoteWeatherService(new HttpClient(new FixedJsonHandler(mutated)){BaseAddress=new Uri("https://example.test/api/")});
                try{
                    await call(client);
                }catch(WeatherProviderException ex){
                    Assert.Equal(ProviderFailure.InvalidResponse,ex.Failure);
                }catch(Exception ex){
                    Assert.Fail($"{name} #{i}: {ex.GetType().Name} が漏れた: {ex.Message}\n{mutated}");
                }
            }
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetObservationsAsync(){
        //観測所は要求元のものを使い、観測値はそのまま戻る
        var h=new RemoteHarness();
        var stations=await h.Client.FindStationsAsync(Sample.Tokyo,3,TestContext.Current.CancellationToken);
        Assert.Equal(Sample.Station,Assert.Single(stations));
        var series=await h.Client.GetObservationsAsync(Sample.Station,Sample.Now.AddDays(-1),Sample.Now,TestContext.Current.CancellationToken);
        Assert.Same(Sample.Station,series.Station);
        Assert.Equal(Sample.Observations().Items,series.Items);
    }
}
