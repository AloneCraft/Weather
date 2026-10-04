namespace Weather.Core;

/// <summary>HTTP レスポンスのキャッシュエントリ。Last-Modified は受信した文字列のまま保持する(MET 規約)。</summary>
public sealed record HttpCacheEntry{
    public required string Key{get;init;}
    public required int StatusCode{get;init;}
    public required byte[] Body{get;init;}
    public string? ContentType{get;init;}
    public string? ETag{get;init;}
    public string? LastModified{get;init;}
    public DateTimeOffset? Date{get;init;}
    public TimeSpan? Age{get;init;}
    public TimeSpan? MaxAge{get;init;}
    public DateTimeOffset? Expires{get;init;}
    public required DateTimeOffset ReceivedAt{get;init;}
    public DateTimeOffset LastAccessedAt{get;init;}
}

public interface IHttpCacheStore{
    ValueTask<HttpCacheEntry?> GetAsync(string key,CancellationToken cancellationToken);
    ValueTask SetAsync(HttpCacheEntry entry,CancellationToken cancellationToken);

    /// <summary>容量上限を超えた分と、長期間使われていないエントリを削除する。</summary>
    ValueTask TrimAsync(long maxBytes,TimeSpan maxUnused,DateTimeOffset now,CancellationToken cancellationToken);
}
