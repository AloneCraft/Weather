namespace Weather.Core;

/// <summary>観測履歴の永続化(History.md)。</summary>
public interface IObservationHistoryStore{
    ValueTask UpsertStationAsync(ObservationStation station,CancellationToken cancellationToken);
    ValueTask<int> InsertObservationsAsync(string stationKey,IEnumerable<Observation> observations,CancellationToken cancellationToken);
    ValueTask UpsertDailySummariesAsync(IEnumerable<DailyObservationSummary> summaries,CancellationToken cancellationToken);
    ValueTask<DateTimeOffset?> GetLastObservedAtAsync(string stationKey,CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<Observation>> GetObservationsAsync(string stationKey,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DailyObservationSummary>> GetDailySummariesAsync(string stationKey,DateOnly from,DateOnly to,CancellationToken cancellationToken);
    ValueTask ThinAsync(DateTimeOffset olderThan,CancellationToken cancellationToken);
    ValueTask PruneAsync(DateTimeOffset olderThan,CancellationToken cancellationToken);
    ValueTask DeleteAllAsync(CancellationToken cancellationToken);
}

public sealed record HistorySyncResult(int Inserted,DateTimeOffset? LastObservedAt,bool Failed);

/// <summary>観測履歴の同期と参照。</summary>
public interface IObservationHistoryService{
    ValueTask<HistorySyncResult> SyncAsync(ObservationStation station,CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<Observation>> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<DailyObservationSummary>> GetDailySummariesAsync(ObservationStation station,DateOnly from,DateOnly to,CancellationToken cancellationToken);
    ValueTask ApplyRetentionAsync(TimeSpan retention,CancellationToken cancellationToken);
    ValueTask DeleteAllAsync(CancellationToken cancellationToken);
}
