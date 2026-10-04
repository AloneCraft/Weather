using System.Collections.Concurrent;
using Weather.Core;

namespace Weather.Providers.Http;

/// <summary>メモリ上のキャッシュストア(テスト・SceneLab・サーバー用)。</summary>
public sealed class MemoryHttpCacheStore:IHttpCacheStore{
    private readonly ConcurrentDictionary<string,HttpCacheEntry> entries=new(StringComparer.Ordinal);

    public int Count=>this.entries.Count;

    public ValueTask<HttpCacheEntry?> GetAsync(string key,CancellationToken cancellationToken){
        this.entries.TryGetValue(key,out var entry);
        return ValueTask.FromResult(entry);
    }

    public ValueTask SetAsync(HttpCacheEntry entry,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(entry);
        this.entries[entry.Key]=entry;
        return ValueTask.CompletedTask;
    }

    public ValueTask TrimAsync(long maxBytes,TimeSpan maxUnused,DateTimeOffset now,CancellationToken cancellationToken){
        foreach(var pair in this.entries){
            if(now-pair.Value.LastAccessedAt>maxUnused){
                this.entries.TryRemove(pair.Key,out _);
            }
        }
        var ordered=this.entries.Values.OrderBy(static e=>e.LastAccessedAt).ToList();
        var total=ordered.Sum(static e=>(long)e.Body.Length);
        foreach(var entry in ordered){
            if(total<=maxBytes){
                break;
            }
            this.entries.TryRemove(entry.Key,out _);
            total-=entry.Body.Length;
        }
        return ValueTask.CompletedTask;
    }
}
