namespace Weather.Core;

/// <summary>取得できなかった補助プロダクトの記録(部分結果の明示)。</summary>
public sealed record DataIssue(ProviderId Provider,string ProductName,ProviderFailure Failure);

/// <summary>地点の予報全体。コンストラクタで不変条件を検証する。</summary>
public sealed class Forecast{
    public Forecast(ResolvedLocation location,IReadOnlyList<ForecastPoint> timeSeries,IReadOnlyList<DailyForecast> daily,string? areaName=null,IReadOnlyList<DataIssue>? issues=null){
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(timeSeries);
        ArgumentNullException.ThrowIfNull(daily);
        ForecastRules.EnsureAscendingAndNonOverlapping(timeSeries);
        ForecastRules.EnsureStrictlyAscending(daily);
        ForecastRules.EnsureJapanUsesJmaOnly(location,timeSeries,daily);
        this.Location=location;
        this.TimeSeries=[..timeSeries];
        this.Daily=[..daily];
        this.AreaName=areaName;
        if(issues is null){
            this.Issues=[];
        }else{
            this.Issues=[..issues];
        }
    }

    public ResolvedLocation Location{get;}
    public string? AreaName{get;}
    public IReadOnlyList<ForecastPoint> TimeSeries{get;}
    public IReadOnlyList<DailyForecast> Daily{get;}
    public IReadOnlyList<DataIssue> Issues{get;}

    public IEnumerable<SourceAttribution> Sources{
        get{
            return this.TimeSeries.Select(static p=>p.Source)
                .Concat(this.Daily.Select(static d=>d.Source))
                .Concat(this.Daily.Select(static d=>d.TemperatureSource).OfType<SourceAttribution>())
                .Distinct();
        }
    }

    public bool IsStale=>this.Sources.Any(static s=>s.IsStale);

    public ForecastPoint? FindPoint(DateTimeOffset time){
        foreach(var point in this.TimeSeries){
            if(point.Contains(time)){
                return point;
            }
        }
        return null;
    }
}

internal static class ForecastRules{
    public static void EnsureAscendingAndNonOverlapping(IReadOnlyList<ForecastPoint> points){
        for(var i=1;i<points.Count;i++){
            if(points[i].Start<points[i-1].End){
                throw new ArgumentException($"時系列が昇順でないか区間が重なっています: {points[i-1].Start:o} / {points[i].Start:o}",nameof(points));
            }
        }
    }

    public static void EnsureStrictlyAscending(IReadOnlyList<DailyForecast> daily){
        for(var i=1;i<daily.Count;i++){
            if(daily[i].Date<=daily[i-1].Date){
                throw new ArgumentException($"日別が日付の昇順でないか重複しています: {daily[i].Date}",nameof(daily));
            }
        }
    }

    public static void EnsureJapanUsesJmaOnly(ResolvedLocation location,IEnumerable<ForecastPoint> timeSeries,IEnumerable<DailyForecast> daily){
        if(location.JmaArea is null&&location.CountryCode!="JP"){
            return;
        }
        var sources=timeSeries.Select(static p=>p.Source)
            .Concat(daily.Select(static d=>d.Source))
            .Concat(daily.Select(static d=>d.TemperatureSource).OfType<SourceAttribution>());
        foreach(var source in sources){
            if(source.Provider!=ProviderId.Jma){
                throw new InvalidOperationException($"日本域に気象庁以外のデータが含まれています: {source.Provider}");
            }
            if(source.Processing.HasFlag(DataProcessing.Aggregated)){
                throw new InvalidOperationException("気象庁データにアプリ側の集計を適用してはいけません。");
            }
        }
    }
}
