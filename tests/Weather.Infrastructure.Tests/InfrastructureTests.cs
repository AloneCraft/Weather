using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Weather.Core;
using Weather.Infrastructure;

namespace Weather.Infrastructure.Tests;

internal sealed class TempDirectory:IDisposable{
    public TempDirectory(){
        this.Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"weather-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.Path);
        this.Options=new InfrastructureOptions{CacheDirectory=System.IO.Path.Combine(this.Path,"cache"),DataDirectory=System.IO.Path.Combine(this.Path,"data")};
    }

    public string Path{get;}
    public InfrastructureOptions Options{get;}

    public void Dispose(){
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try{
            Directory.Delete(this.Path,true);
        }catch(IOException){
            //一時ファイルの削除失敗はテスト結果に影響させない
        }
    }
}

public class FileHttpCacheStore{
    private static readonly DateTimeOffset Now=new(2026,10,4,0,0,0,TimeSpan.Zero);

    [Fact,Trait("Category","Unit")]public async Task GetAsync(){
        using var temp=new TempDirectory();
        var store=new Infrastructure.FileHttpCacheStore(temp.Options);
        {
            //保存した値を取り出せる(Last-Modified は文字列のまま)
            await store.SetAsync(Entry("a",[1,2,3]),TestContext.Current.CancellationToken);
            var entry=await store.GetAsync("a",TestContext.Current.CancellationToken);
            Assert.NotNull(entry);
            Assert.Equal(new byte[]{1,2,3},entry!.Body);
            Assert.Equal("Sat, 03 Oct 2026 21:42:41 GMT",entry.LastModified);
            Assert.Equal(TimeSpan.FromSeconds(60),entry.MaxAge);
        }
        {
            //ないキーは null
            Assert.Null(await store.GetAsync("missing",TestContext.Current.CancellationToken));
        }
    }

    [Fact,Trait("Category","Unit")]public async Task TrimAsync(){
        using var temp=new TempDirectory();
        var store=new Infrastructure.FileHttpCacheStore(temp.Options);
        {
            //長期間使われていないエントリと、容量上限を超えた古いエントリを削除する
            await store.SetAsync(Entry("old",new byte[10])with{LastAccessedAt=Now.AddDays(-20)},TestContext.Current.CancellationToken);
            await store.SetAsync(Entry("a",new byte[100])with{LastAccessedAt=Now.AddHours(-2)},TestContext.Current.CancellationToken);
            await store.SetAsync(Entry("b",new byte[100])with{LastAccessedAt=Now.AddHours(-1)},TestContext.Current.CancellationToken);
            await store.TrimAsync(150,TimeSpan.FromDays(14),Now,TestContext.Current.CancellationToken);
            Assert.Null(await store.GetAsync("old",TestContext.Current.CancellationToken));
            Assert.Null(await store.GetAsync("a",TestContext.Current.CancellationToken));
            Assert.NotNull(await store.GetAsync("b",TestContext.Current.CancellationToken));
        }
    }

    private static HttpCacheEntry Entry(string key,byte[] body){
        return new HttpCacheEntry{Key=key,StatusCode=200,Body=body,LastModified="Sat, 03 Oct 2026 21:42:41 GMT",MaxAge=TimeSpan.FromSeconds(60),ReceivedAt=Now,LastAccessedAt=Now};
    }
}

public class WeatherDatabase{
    [Fact,Trait("Category","Unit")]public async Task OpenAsync(){
        using var temp=new TempDirectory();
        {
            //初回にマイグレーションを適用し、user_version が最新になる
            var db=new Infrastructure.WeatherDatabase(temp.Options);
            await using var connection=await db.OpenAsync(TestContext.Current.CancellationToken);
            await using var command=connection.CreateCommand();
            command.CommandText="PRAGMA user_version;";
            Assert.Equal((long)Infrastructure.WeatherDatabase.SchemaVersion,(long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        }
        {
            //再度開いてもマイグレーションを重ねて適用しない
            var db=new Infrastructure.WeatherDatabase(temp.Options);
            await using var connection=await db.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(System.Data.ConnectionState.Open,connection.State);
        }
    }
}

public class SqliteFavoritesStore{
    [Fact,Trait("Category","Unit")]public async Task GetAllAsync(){
        using var temp=new TempDirectory();
        var store=new Infrastructure.SqliteFavoritesStore(new Infrastructure.WeatherDatabase(temp.Options));
        var ct=TestContext.Current.CancellationToken;
        {
            //保存・並べ替え・削除
            await store.SaveAsync(new FavoritePlace("a","東京",new GeoPoint(35.69,139.75),0),ct);
            await store.SaveAsync(new FavoritePlace("b","大阪",new GeoPoint(34.69,135.5),1){StationKey="Jma:62078"},ct);
            await store.ReorderAsync(["b","a"],ct);
            var all=await store.GetAllAsync(ct);
            Assert.Equal(["b","a"],all.Select(static p=>p.Id).ToArray());
            Assert.Equal("Jma:62078",all[0].StationKey);
            await store.DeleteAsync("b",ct);
            Assert.Single(await store.GetAllAsync(ct));
        }
    }
}

public class SqliteObservationHistoryStore{
    private static readonly ObservationStation Jma=new("44132",ProviderId.Jma,"東京",new GeoPoint(35.69,139.75),25);
    private static readonly ObservationStation Nws=new("KDCA",ProviderId.Nws,"DCA",new GeoPoint(38.85,-77.03),5);
    private static readonly DateTimeOffset Base=new(2026,9,1,0,0,0,TimeSpan.Zero);

    [Fact,Trait("Category","Unit")]public async Task InsertObservationsAsync(){
        using var temp=new TempDirectory();
        var store=new Infrastructure.SqliteObservationHistoryStore(new Infrastructure.WeatherDatabase(temp.Options));
        var ct=TestContext.Current.CancellationToken;
        await store.UpsertStationAsync(Jma,ct);
        var items=Enumerable.Range(0,6).Select(i=>new Observation(Base.AddMinutes(10*i)){TemperatureC=new Measurement(20+i,QualityOf(i))}).ToList();
        {
            //同じ行を何度入れても増えない(冪等)
            Assert.Equal(6,await store.InsertObservationsAsync(Jma.Key,items,ct));
            Assert.Equal(0,await store.InsertObservationsAsync(Jma.Key,items,ct));
        }
        {
            //Suspect の品質を保持する
            var read=await store.GetObservationsAsync(Jma.Key,Base,Base.AddHours(1),ct);
            Assert.Equal(6,read.Count);
            Assert.Equal(MeasurementQuality.Suspect,read[3].TemperatureC!.Value.Quality);
            Assert.Equal(Base.AddMinutes(50),await store.GetLastObservedAtAsync(Jma.Key,ct));
        }
    }

    private static MeasurementQuality QualityOf(int i){
        if(i==3){
            return MeasurementQuality.Suspect;
        }
        return MeasurementQuality.Normal;
    }

    [Fact,Trait("Category","Unit")]public async Task ThinAsync(){
        using var temp=new TempDirectory();
        var store=new Infrastructure.SqliteObservationHistoryStore(new Infrastructure.WeatherDatabase(temp.Options));
        var ct=TestContext.Current.CancellationToken;
        await store.UpsertStationAsync(Jma,ct);
        await store.UpsertStationAsync(Nws,ct);
        var jmaItems=Enumerable.Range(0,12).Select(i=>new Observation(Base.AddMinutes(10*i)){TemperatureC=new Measurement(i)}).ToList();
        var nwsItems=new[]{Base.AddMinutes(10),Base.AddMinutes(52),Base.AddMinutes(70),Base.AddMinutes(112)}.Select(t=>new Observation(t){TemperatureC=new Measurement(1)}).ToList();
        await store.InsertObservationsAsync(Jma.Key,jmaItems,ct);
        await store.InsertObservationsAsync(Nws.Key,nwsItems,ct);
        await store.ThinAsync(Base.AddDays(1),ct);
        {
            //気象庁は正時の行だけ残り、値は元のまま(部分集合)
            var jma=await store.GetObservationsAsync(Jma.Key,Base,Base.AddDays(1),ct);
            Assert.Equal([Base,Base.AddHours(1)],jma.Select(static o=>o.ObservedAt).ToArray());
            Assert.Equal(6,jma[1].TemperatureC!.Value.Value);
        }
        {
            //NWS は各時間帯の最後の観測を残す
            var nws=await store.GetObservationsAsync(Nws.Key,Base,Base.AddDays(1),ct);
            Assert.Equal([Base.AddMinutes(52),Base.AddMinutes(112)],nws.Select(static o=>o.ObservedAt).ToArray());
        }
    }

    [Fact,Trait("Category","Unit")]public async Task PruneAsync(){
        using var temp=new TempDirectory();
        var store=new Infrastructure.SqliteObservationHistoryStore(new Infrastructure.WeatherDatabase(temp.Options));
        var ct=TestContext.Current.CancellationToken;
        await store.UpsertStationAsync(Jma,ct);
        await store.InsertObservationsAsync(Jma.Key,[new Observation(Base),new Observation(Base.AddDays(10))],ct);
        await store.UpsertDailySummariesAsync([new DailyObservationSummary(Jma.Key,new DateOnly(2026,9,1)){MaxTempC=30}],ct);
        {
            //保持期間外の行が残らない
            await store.PruneAsync(Base.AddDays(5),ct);
            var left=await store.GetObservationsAsync(Jma.Key,Base,Base.AddDays(20),ct);
            Assert.Single(left);
            Assert.Empty(await store.GetDailySummariesAsync(Jma.Key,new DateOnly(2026,8,1),new DateOnly(2026,12,31),ct));
        }
    }
}

public class ObservationHistoryService{
    private static readonly DateTimeOffset Now=new(2026,10,4,0,0,0,TimeSpan.Zero);

    [Fact,Trait("Category","Unit")]public async Task SyncAsync(){
        using var temp=new TempDirectory();
        var ct=TestContext.Current.CancellationToken;
        var weather=new FakeWeatherService();
        var store=new Infrastructure.SqliteObservationHistoryStore(new Infrastructure.WeatherDatabase(temp.Options));
        var service=new Infrastructure.ObservationHistoryService(weather,store,new FakeTimeProvider(Now),NullLogger<Infrastructure.ObservationHistoryService>.Instance);
        var station=new ObservationStation("KDCA",ProviderId.Nws,"DCA",new GeoPoint(38.85,-77.03),5);
        {
            //初回はサーバーの保持期間(7 日)から取得し、NWS の日最高・最低は集計値(IsDerived)
            var result=await service.SyncAsync(station,ct);
            Assert.False(result.Failed);
            Assert.Equal(3,result.Inserted);
            Assert.Equal(Now.AddDays(-7),weather.LastFrom);
            var summaries=await service.GetDailySummariesAsync(station,new DateOnly(2026,10,1),new DateOnly(2026,10,4),ct);
            Assert.All(summaries,static s=>Assert.True(s.IsDerived));
        }
        {
            //2 回目は最後の観測の直後から。重複は入らない
            var result=await service.SyncAsync(station,ct);
            Assert.Equal(0,result.Inserted);
            Assert.True(weather.LastFrom>Now.AddHours(-3));
        }
        {
            //取得に失敗しても例外にせず Failed を返す
            weather.Fail=true;
            var result=await service.SyncAsync(station,ct);
            Assert.True(result.Failed);
        }
    }

    private sealed class FakeWeatherService:IWeatherService{
        public bool Fail{get;set;}
        public DateTimeOffset LastFrom{get;private set;}

        public ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
            this.LastFrom=from;
            if(this.Fail){
                throw new WeatherProviderException(ProviderId.Nws,ProviderFailure.Network,"offline");
            }
            var items=new[]{Now.AddHours(-3),Now.AddHours(-2),Now.AddHours(-1)}
                .Where(t=>t>=from&&t<=to)
                .Select(static (t,i)=>new Observation(t){TemperatureC=new Measurement(10+i)})
                .ToList();
            var source=new SourceAttribution{Provider=ProviderId.Nws,AgencyName="NWS",ProductName="Observations",RetrievedAt=Now,License=new LicenseInfo("t",null)};
            return ValueTask.FromResult(new ObservationSeries(station,items,source));
        }

        public TimeSpan GetObservationServerRetention(ProviderId provider){
            return TimeSpan.FromDays(7);
        }

        public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
            throw new NotSupportedException();
        }

        public ValueTask<ForecastResult> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
            throw new NotSupportedException();
        }

        public ValueTask<AlertResult> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
            throw new NotSupportedException();
        }

        public Availability GetObservationAvailability(ResolvedLocation location){
            return Availability.Available;
        }

        public ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
            throw new NotSupportedException();
        }
    }
}
