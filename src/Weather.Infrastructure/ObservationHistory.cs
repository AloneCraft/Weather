using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Weather.Core;

namespace Weather.Infrastructure;

/// <summary>観測履歴の永続化(History.md)。値は書き換えずに保存し、間引きは行の削除だけで行う。</summary>
public sealed class SqliteObservationHistoryStore(WeatherDatabase database):IObservationHistoryStore{
    private static readonly string[] Columns=["temperature_c","humidity_pct","precip_10m_mm","precip_1h_mm","precip_24h_mm","wind_speed_ms","wind_dir_deg","wind_gust_ms","pressure_hpa","sea_level_hpa","sunshine_1h_h","snow_depth_cm","visibility_m"];

    public async ValueTask UpsertStationAsync(ObservationStation station,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO stations(station_key,provider,station_id,name,latitude,longitude,elevation_m)
            VALUES($key,$provider,$id,$name,$lat,$lon,$elev)
            ON CONFLICT(station_key) DO UPDATE SET name=$name,latitude=$lat,longitude=$lon,elevation_m=$elev;
            """;
        command.Parameters.AddWithValue("$key",station.Key);
        command.Parameters.AddWithValue("$provider",(int)station.Provider);
        command.Parameters.AddWithValue("$id",station.Id);
        command.Parameters.AddWithValue("$name",station.Name);
        command.Parameters.AddWithValue("$lat",station.Location.Latitude);
        command.Parameters.AddWithValue("$lon",station.Location.Longitude);
        command.Parameters.AddWithValue("$elev",(object?)station.ElevationM??DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>主キーが重複する行は無視する(何度実行しても同じ結果)。</summary>
    public async ValueTask<int> InsertObservationsAsync(string stationKey,IEnumerable<Observation> observations,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(observations);
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.Transaction=transaction;
        command.CommandText=$"""
            INSERT OR IGNORE INTO observations(station_key,observed_at_utc,{string.Join(',',Columns)},suspect_mask,weather_text)
            VALUES($key,$at,{string.Join(',',Columns.Select(static c=>"$"+c))},$mask,$text);
            """;
        var key=command.Parameters.Add("$key",SqliteType.Text);
        var at=command.Parameters.Add("$at",SqliteType.Integer);
        var values=Columns.Select(c=>command.Parameters.Add("$"+c,SqliteType.Real)).ToArray();
        var mask=command.Parameters.Add("$mask",SqliteType.Integer);
        var text=command.Parameters.Add("$text",SqliteType.Text);
        var inserted=0;
        foreach(var o in observations){
            key.Value=stationKey;
            at.Value=o.ObservedAt.ToUnixTimeSeconds();
            var measurements=Measurements(o);
            var suspect=0;
            for(var i=0;i<Columns.Length;i++){
                if(measurements[i] is {} m){
                    values[i].Value=m.Value;
                    if(m.Quality==MeasurementQuality.Suspect){
                        suspect|=1<<i;
                    }
                }else{
                    values[i].Value=DBNull.Value;
                }
            }
            mask.Value=suspect;
            text.Value=(object?)o.WeatherText??DBNull.Value;
            inserted+=await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async ValueTask UpsertDailySummariesAsync(IEnumerable<DailyObservationSummary> summaries,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(summaries);
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach(var s in summaries){
            await using var command=connection.CreateCommand();
            command.Transaction=transaction;
            command.CommandText="""
                INSERT INTO daily_summaries(station_key,local_date,max_temp_c,max_temp_at,min_temp_c,min_temp_at,precip_mm,is_derived)
                VALUES($key,$date,$max,$maxAt,$min,$minAt,$precip,$derived)
                ON CONFLICT(station_key,local_date) DO UPDATE SET max_temp_c=$max,max_temp_at=$maxAt,min_temp_c=$min,min_temp_at=$minAt,precip_mm=$precip,is_derived=$derived;
                """;
            command.Parameters.AddWithValue("$key",s.StationKey);
            command.Parameters.AddWithValue("$date",s.LocalDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$max",(object?)s.MaxTempC??DBNull.Value);
            command.Parameters.AddWithValue("$maxAt",(object?)s.MaxTempAt?.ToUnixTimeSeconds()??DBNull.Value);
            command.Parameters.AddWithValue("$min",(object?)s.MinTempC??DBNull.Value);
            command.Parameters.AddWithValue("$minAt",(object?)s.MinTempAt?.ToUnixTimeSeconds()??DBNull.Value);
            command.Parameters.AddWithValue("$precip",(object?)s.PrecipitationMm??DBNull.Value);
            command.Parameters.AddWithValue("$derived",s.IsDerived);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DateTimeOffset?> GetLastObservedAtAsync(string stationKey,CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT MAX(observed_at_utc) FROM observations WHERE station_key=$key;";
        command.Parameters.AddWithValue("$key",stationKey);
        var value=await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if(value is null||value is DBNull){
            return null;
        }
        return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(value,CultureInfo.InvariantCulture));
    }

    public async ValueTask<IReadOnlyList<Observation>> GetObservationsAsync(string stationKey,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText=$"SELECT observed_at_utc,{string.Join(',',Columns)},suspect_mask,weather_text FROM observations WHERE station_key=$key AND observed_at_utc BETWEEN $from AND $to ORDER BY observed_at_utc;";
        command.Parameters.AddWithValue("$key",stationKey);
        command.Parameters.AddWithValue("$from",from.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$to",to.ToUnixTimeSeconds());
        var list=new List<Observation>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){
            var suspect=reader.GetInt32(Columns.Length+1);
            Measurement? Read(int i){
                if(reader.IsDBNull(i+1)){
                    return null;
                }
                var quality=MeasurementQuality.Normal;
                if((suspect&(1<<i))!=0){
                    quality=MeasurementQuality.Suspect;
                }
                return new Measurement(reader.GetDouble(i+1),quality);
            }
            string? text=null;
            if(!reader.IsDBNull(Columns.Length+2)){
                text=reader.GetString(Columns.Length+2);
            }
            list.Add(new Observation(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0))){
                TemperatureC=Read(0),
                HumidityPercent=Read(1),
                Precipitation10mMm=Read(2),
                Precipitation1hMm=Read(3),
                Precipitation24hMm=Read(4),
                WindSpeedMs=Read(5),
                WindDirectionDeg=Read(6),
                WindGustMs=Read(7),
                PressureHpa=Read(8),
                SeaLevelPressureHpa=Read(9),
                Sunshine1hHours=Read(10),
                SnowDepthCm=Read(11),
                VisibilityM=Read(12),
                WeatherText=text,
            });
        }
        return list;
    }

    public async ValueTask<IReadOnlyList<DailyObservationSummary>> GetDailySummariesAsync(string stationKey,DateOnly from,DateOnly to,CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT local_date,max_temp_c,max_temp_at,min_temp_c,min_temp_at,precip_mm,is_derived FROM daily_summaries WHERE station_key=$key AND local_date BETWEEN $from AND $to ORDER BY local_date;";
        command.Parameters.AddWithValue("$key",stationKey);
        command.Parameters.AddWithValue("$from",from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to",to.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        var list=new List<DailyObservationSummary>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){
            list.Add(new DailyObservationSummary(stationKey,DateOnly.ParseExact(reader.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture)){
                MaxTempC=NullableDouble(reader,1),
                MaxTempAt=NullableTime(reader,2),
                MinTempC=NullableDouble(reader,3),
                MinTempAt=NullableTime(reader,4),
                PrecipitationMm=NullableDouble(reader,5),
                IsDerived=reader.GetBoolean(6),
            });
        }
        return list;
    }

    /// <summary>
    /// 間引き(History.md「間引きと集計」)。値は計算し直さず、行の削除だけで行う。
    /// 気象庁は正時(:00)の行を残す。NWS は各時間帯の最後の観測(定時観測は :52 前後)を残す。
    /// </summary>
    public async ValueTask ThinAsync(DateTimeOffset olderThan,CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            DELETE FROM observations WHERE observed_at_utc<$t AND station_key LIKE 'Jma:%' AND (observed_at_utc%3600)<>0;
            DELETE FROM observations WHERE observed_at_utc<$t AND station_key NOT LIKE 'Jma:%'
                AND observed_at_utc<>(SELECT MAX(i.observed_at_utc) FROM observations i
                    WHERE i.station_key=observations.station_key AND i.observed_at_utc/3600=observations.observed_at_utc/3600);
            """;
        command.Parameters.AddWithValue("$t",olderThan.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PruneAsync(DateTimeOffset olderThan,CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="DELETE FROM observations WHERE observed_at_utc<$t; DELETE FROM daily_summaries WHERE local_date<$d;";
        command.Parameters.AddWithValue("$t",olderThan.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$d",DateOnly.FromDateTime(olderThan.UtcDateTime).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAllAsync(CancellationToken cancellationToken){
        await using var connection=await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command=connection.CreateCommand();
        command.CommandText="DELETE FROM observations; DELETE FROM daily_summaries; DELETE FROM sync_state;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Measurement?[] Measurements(Observation o){
        return [o.TemperatureC,o.HumidityPercent,o.Precipitation10mMm,o.Precipitation1hMm,o.Precipitation24hMm,o.WindSpeedMs,o.WindDirectionDeg,o.WindGustMs,o.PressureHpa,o.SeaLevelPressureHpa,o.Sunshine1hHours,o.SnowDepthCm,o.VisibilityM];
    }

    private static double? NullableDouble(SqliteDataReader reader,int i){
        if(reader.IsDBNull(i)){
            return null;
        }
        return reader.GetDouble(i);
    }

    private static DateTimeOffset? NullableTime(SqliteDataReader reader,int i){
        if(reader.IsDBNull(i)){
            return null;
        }
        return DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(i));
    }
}

/// <summary>
/// 観測履歴の同期と参照(History.md「同期の手順」)。
/// サーバーに残っている範囲から欠けた分を補い、補間はしない。
/// </summary>
public sealed partial class ObservationHistoryService(IWeatherService weather,IObservationHistoryStore store,TimeProvider time,ILogger<ObservationHistoryService> logger):IObservationHistoryService{
    public static readonly TimeSpan RawRetention=TimeSpan.FromDays(30);

    public async ValueTask<HistorySyncResult> SyncAsync(ObservationStation station,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        var now=time.GetUtcNow();
        var retention=weather.GetObservationServerRetention(station.Provider);
        var last=await store.GetLastObservedAtAsync(station.Key,cancellationToken).ConfigureAwait(false);
        var from=now-retention;
        if(last is {} l&&l>from){
            from=l.AddSeconds(1);
        }
        await store.UpsertStationAsync(station,cancellationToken).ConfigureAwait(false);
        ObservationSeries series;
        try{
            series=await weather.GetObservationsAsync(station,from,now,cancellationToken).ConfigureAwait(false);
        }catch(WeatherProviderException ex){
            //同期の失敗は画面の表示をブロックしない(取得済みの範囲を表示する)
            LogSyncFailed(logger,station.Key,ex);
            return new HistorySyncResult(0,last,true);
        }
        var inserted=await store.InsertObservationsAsync(station.Key,series.Items,cancellationToken).ConfigureAwait(false);
        if(series.DailySummaries.Count>0){
            await store.UpsertDailySummariesAsync(series.DailySummaries,cancellationToken).ConfigureAwait(false);
        }else if(series.Items.Count>0){
            await store.UpsertDailySummariesAsync(await this.DeriveSummariesAsync(station,series.Items,cancellationToken).ConfigureAwait(false),cancellationToken).ConfigureAwait(false);
        }
        await store.ThinAsync(now-RawRetention,cancellationToken).ConfigureAwait(false);
        var newest=last;
        if(series.Items.Count>0&&(newest is null||series.Items[^1].ObservedAt>newest)){
            newest=series.Items[^1].ObservedAt;
        }
        return new HistorySyncResult(inserted,newest,false);
    }

    public ValueTask<IReadOnlyList<Observation>> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        return store.GetObservationsAsync(station.Key,from,to,cancellationToken);
    }

    public ValueTask<IReadOnlyList<DailyObservationSummary>> GetDailySummariesAsync(ObservationStation station,DateOnly from,DateOnly to,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        return store.GetDailySummariesAsync(station.Key,from,to,cancellationToken);
    }

    public ValueTask ApplyRetentionAsync(TimeSpan retention,CancellationToken cancellationToken){
        return store.PruneAsync(time.GetUtcNow()-retention,cancellationToken);
    }

    public ValueTask DeleteAllAsync(CancellationToken cancellationToken){
        return store.DeleteAllAsync(cancellationToken);
    }

    /// <summary>NWS の日最高・最低は保存した観測から計算する(IsDerived、UI に「アプリで集計」)。</summary>
    private async Task<List<DailyObservationSummary>> DeriveSummariesAsync(ObservationStation station,IReadOnlyList<Observation> items,CancellationToken cancellationToken){
        var dates=items.Select(static o=>DateOnly.FromDateTime(o.ObservedAt.UtcDateTime)).Distinct().ToList();
        var result=new List<DailyObservationSummary>();
        foreach(var date in dates){
            var start=new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero);
            var day=await store.GetObservationsAsync(station.Key,start,start.AddDays(1).AddSeconds(-1),cancellationToken).ConfigureAwait(false);
            var temps=day.Where(static o=>o.TemperatureC is not null).ToList();
            if(temps.Count==0){
                continue;
            }
            var max=temps.MaxBy(static o=>o.TemperatureC!.Value.Value)!;
            var min=temps.MinBy(static o=>o.TemperatureC!.Value.Value)!;
            result.Add(new DailyObservationSummary(station.Key,date){
                MaxTempC=max.TemperatureC!.Value.Value,
                MaxTempAt=max.ObservedAt,
                MinTempC=min.TemperatureC!.Value.Value,
                MinTempAt=min.ObservedAt,
                IsDerived=true,
            });
        }
        return result;
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="観測履歴の同期に失敗しました: {Station}")]
    private static partial void LogSyncFailed(ILogger logger,string station,Exception exception);
}

public static class InfrastructureServiceCollectionExtensions{
    /// <summary>端末側の永続化を登録する(HTTP キャッシュストア・お気に入り・観測履歴)。</summary>
    public static IServiceCollection AddWeatherInfrastructure(this IServiceCollection services,Action<InfrastructureOptions> configure){
        ArgumentNullException.ThrowIfNull(configure);
        var options=new InfrastructureOptions();
        configure(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IHttpCacheStore,FileHttpCacheStore>());
        services.TryAddSingleton<WeatherDatabase>();
        services.TryAddSingleton<IFavoritesStore,SqliteFavoritesStore>();
        services.TryAddSingleton<IObservationHistoryStore,SqliteObservationHistoryStore>();
        services.TryAddSingleton<IObservationHistoryService,ObservationHistoryService>();
        return services;
    }
}
