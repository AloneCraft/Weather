using Microsoft.Extensions.Time.Testing;
using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.ViewModels;

namespace Weather.Presentation.Tests;

/// <summary>期間ごとに観測と日別の要約を返す(同期で完了する)。</summary>
internal sealed class StubHistory:IObservationHistoryService{
    public Func<DateTimeOffset,DateTimeOffset,IReadOnlyList<Observation>> Observations{get;set;}=static (_,_)=>[];

    /// <summary>設定すると、観測の取得はこの完了を待つ(完了順の制御用)。</summary>
    public Func<DateTimeOffset,DateTimeOffset,Task>? Gate{get;set;}
    public List<DailyObservationSummary> Summaries{get;}=[];

    public ValueTask<HistorySyncResult> SyncAsync(ObservationStation station,CancellationToken cancellationToken){
        return ValueTask.FromResult(new HistorySyncResult(0,null,false));
    }

    public async ValueTask<IReadOnlyList<Observation>> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        if(this.Gate is not null){
            await this.Gate(from,to);
        }
        return this.Observations(from,to);
    }

    public ValueTask<IReadOnlyList<DailyObservationSummary>> GetDailySummariesAsync(ObservationStation station,DateOnly from,DateOnly to,CancellationToken cancellationToken){
        IReadOnlyList<DailyObservationSummary> list=[..this.Summaries.Where(s=>s.LocalDate>=from&&s.LocalDate<=to).OrderBy(static s=>s.LocalDate)];
        return ValueTask.FromResult(list);
    }

    public ValueTask ApplyRetentionAsync(TimeSpan retention,CancellationToken cancellationToken){
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAllAsync(CancellationToken cancellationToken){
        return ValueTask.CompletedTask;
    }
}

public class HistoryViewModel{
    private static readonly ObservationStation Jma=new("44132",ProviderId.Jma,"東京",new GeoPoint(35.69,139.75),25);
    private static readonly ObservationStation Nws=new("KDCA",ProviderId.Nws,"Washington",new GeoPoint(38.85,-77.04),3);

    private static Presentation.ViewModels.HistoryViewModel Create(StubHistory history){
        var settings=new Presentation.AppSettings(new FakeSettingsStore()){Units=UnitSystem.Metric};
        return new Presentation.ViewModels.HistoryViewModel(new WeatherSession(),new FakeWeatherService(),history,new FakeFavorites(),settings,new FakeTimeProvider(Sample.Now));
    }

    [Fact,Trait("Category","Unit")]public async Task LoadAsync_Race(){
        //範囲を素早く切り替えて、先に始めた読み込みが後で終わっても、最後に選んだ範囲の内容が残る
        var history=new StubHistory();
        history.Observations=static (from,to)=>{
            //期間の長さで中身を区別する(週は 1 件・年は 2 件)
            if(to-from>TimeSpan.FromDays(100)){
                return [new Observation(Sample.Now.AddDays(-200)){TemperatureC=new Measurement(1)},new Observation(Sample.Now.AddDays(-100)){TemperatureC=new Measurement(2)}];
            }
            return [new Observation(Sample.Now.AddDays(-3)){TemperatureC=new Measurement(30)}];
        };
        var week=new TaskCompletionSource();
        var year=new TaskCompletionSource();
        var armed=false;
        history.Gate=(from,to)=>{
            if(!armed){
                return Task.CompletedTask;
            }
            if(to-from>TimeSpan.FromDays(100)){
                return year.Task;
            }
            return week.Task;
        };
        var vm=Create(history);
        vm.Station=Jma;
        await vm.LoadAsync();
        armed=true;
        vm.Range=HistoryRange.Week;
        vm.Range=HistoryRange.Year;
        //年が先に終わり、週が後に終わる
        year.SetResult();
        await Task.Delay(100,TestContext.Current.CancellationToken);
        week.SetResult();
        await Task.Delay(100,TestContext.Current.CancellationToken);
        Assert.Equal(HistoryRange.Year,vm.Range);
        Assert.Equal(2,vm.Chart.Count);
        Assert.Equal(2,vm.Rows.Count);
    }

    [Fact,Trait("Category","Unit")]public async Task LoadAsync(){
        {
            //今日の要約がまだなければ、昨日の値を「今日の最高/最低」として表示しない
            var history=new StubHistory();
            history.Summaries.Add(new DailyObservationSummary(Jma.Key,new DateOnly(2026,10,3)){MaxTempC=25,MinTempC=15});
            var vm=Create(history);
            vm.Station=Jma;
            await vm.LoadAsync();
            Assert.Null(vm.SummaryText);
        }
        {
            //今日の要約があれば今日の値を表示する(昨日の値ではない)
            var history=new StubHistory();
            history.Summaries.Add(new DailyObservationSummary(Jma.Key,new DateOnly(2026,10,3)){MaxTempC=25,MinTempC=15});
            history.Summaries.Add(new DailyObservationSummary(Jma.Key,new DateOnly(2026,10,4)){MaxTempC=28,MinTempC=17});
            var vm=Create(history);
            vm.Station=Jma;
            await vm.LoadAsync();
            Assert.Contains("28",vm.SummaryText,StringComparison.Ordinal);
            Assert.DoesNotContain("25",vm.SummaryText,StringComparison.Ordinal);
        }
        {
            //範囲を切り替えて観測が見つかったら、「保存済みの観測はまだありません」を消す
            var history=new StubHistory();
            history.Observations=static (from,to)=>{
                if(to-from>TimeSpan.FromDays(2)){
                    return [new Observation(Sample.Now.AddDays(-3)){TemperatureC=new Measurement(20)}];
                }
                return [];
            };
            var vm=Create(history);
            vm.Station=Jma;
            await vm.LoadAsync();
            Assert.NotNull(vm.Message);
            vm.Range=HistoryRange.Month;
            Assert.NotEmpty(vm.Rows);
            Assert.Null(vm.Message);
        }
        {
            //降水量の棒: 気象庁(10 分値)は正時の行、NWS(毎時 52 分前後)は時間帯の最後の観測を使う
            var jma=new StubHistory();
            jma.Observations=static (_,_)=>[
                new Observation(Sample.Now.AddMinutes(-60)){Precipitation1hMm=new Measurement(4)},
                new Observation(Sample.Now.AddMinutes(-50)){Precipitation1hMm=new Measurement(3)},
            ];
            var jmaVm=Create(jma);
            jmaVm.Station=Jma;
            await jmaVm.LoadAsync();
            Assert.Equal([4d,null],jmaVm.Chart.Select(static p=>p.Precipitation));
            var nws=new StubHistory();
            nws.Observations=static (_,_)=>[
                new Observation(new DateTimeOffset(2026,10,4,1,52,0,TimeSpan.Zero)){Precipitation1hMm=new Measurement(1)},
                new Observation(new DateTimeOffset(2026,10,4,2,52,0,TimeSpan.Zero)){Precipitation1hMm=new Measurement(2)},
            ];
            var nwsVm=Create(nws);
            nwsVm.Station=Nws;
            await nwsVm.LoadAsync();
            Assert.Equal([1d,2d],nwsVm.Chart.Select(static p=>p.Precipitation));
        }
    }
}
