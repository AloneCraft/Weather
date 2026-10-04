using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weather.Core;

namespace Weather.Infrastructure;

public sealed class InfrastructureOptions{
    /// <summary>OS が消してよいキャッシュの置き場所(MAUI: FileSystem.CacheDirectory)。</summary>
    public string CacheDirectory{get;set;}=Path.Combine(Path.GetTempPath(),"Weather","cache");

    /// <summary>永続データの置き場所(MAUI: FileSystem.AppDataDirectory)。</summary>
    public string DataDirectory{get;set;}=Path.Combine(Path.GetTempPath(),"Weather","data");

    public string DatabasePath=>Path.Combine(this.DataDirectory,"weather.db");
}

internal sealed record HttpCacheMetadata(
    string Key,
    int StatusCode,
    string? ContentType,
    string? ETag,
    string? LastModified,
    DateTimeOffset? Date,
    TimeSpan? Age,
    TimeSpan? MaxAge,
    DateTimeOffset? Expires,
    DateTimeOffset ReceivedAt,
    DateTimeOffset LastAccessedAt,
    long Length);

[JsonSerializable(typeof(HttpCacheMetadata))]
internal sealed partial class InfrastructureJsonContext:JsonSerializerContext;

/// <summary>
/// ファイルの HTTP キャッシュストア(CacheAndRouting.md)。
/// 本文とメタデータを別ファイルに保存する。JSON は source generator(AOT / トリミング安全)。
/// </summary>
public sealed class FileHttpCacheStore:IHttpCacheStore{
    private readonly string directory;

    public FileHttpCacheStore(InfrastructureOptions options){
        ArgumentNullException.ThrowIfNull(options);
        this.directory=Path.Combine(options.CacheDirectory,"http");
        Directory.CreateDirectory(this.directory);
    }

    public async ValueTask<HttpCacheEntry?> GetAsync(string key,CancellationToken cancellationToken){
        var (bodyPath,metaPath)=this.Paths(key);
        if(!File.Exists(metaPath)||!File.Exists(bodyPath)){
            return null;
        }
        HttpCacheMetadata? meta;
        try{
            await using var stream=File.OpenRead(metaPath);
            meta=await JsonSerializer.DeserializeAsync(stream,InfrastructureJsonContext.Default.HttpCacheMetadata,cancellationToken).ConfigureAwait(false);
        }catch(JsonException){
            //壊れたメタデータはエントリごと捨てる
            Delete(bodyPath,metaPath);
            return null;
        }
        if(meta is null||meta.Key!=key){
            return null;
        }
        var body=await File.ReadAllBytesAsync(bodyPath,cancellationToken).ConfigureAwait(false);
        return new HttpCacheEntry{
            Key=meta.Key,
            StatusCode=meta.StatusCode,
            Body=body,
            ContentType=meta.ContentType,
            ETag=meta.ETag,
            LastModified=meta.LastModified,
            Date=meta.Date,
            Age=meta.Age,
            MaxAge=meta.MaxAge,
            Expires=meta.Expires,
            ReceivedAt=meta.ReceivedAt,
            LastAccessedAt=meta.LastAccessedAt,
        };
    }

    public async ValueTask SetAsync(HttpCacheEntry entry,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(entry);
        var (bodyPath,metaPath)=this.Paths(entry.Key);
        var meta=new HttpCacheMetadata(entry.Key,entry.StatusCode,entry.ContentType,entry.ETag,entry.LastModified,entry.Date,entry.Age,entry.MaxAge,entry.Expires,entry.ReceivedAt,entry.LastAccessedAt,entry.Body.Length);
        var tempBody=bodyPath+".tmp";
        var tempMeta=metaPath+".tmp";
        await File.WriteAllBytesAsync(tempBody,entry.Body,cancellationToken).ConfigureAwait(false);
        await using(var stream=File.Create(tempMeta)){
            await JsonSerializer.SerializeAsync(stream,meta,InfrastructureJsonContext.Default.HttpCacheMetadata,cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempBody,bodyPath,true);
        File.Move(tempMeta,metaPath,true);
    }

    public async ValueTask TrimAsync(long maxBytes,TimeSpan maxUnused,DateTimeOffset now,CancellationToken cancellationToken){
        var entries=new List<(string Body,string Meta,DateTimeOffset Accessed,long Length)>();
        foreach(var metaPath in Directory.EnumerateFiles(this.directory,"*.meta")){
            var bodyPath=Path.ChangeExtension(metaPath,".body");
            try{
                await using var stream=File.OpenRead(metaPath);
                var meta=await JsonSerializer.DeserializeAsync(stream,InfrastructureJsonContext.Default.HttpCacheMetadata,cancellationToken).ConfigureAwait(false);
                if(meta is null){
                    continue;
                }
                entries.Add((bodyPath,metaPath,meta.LastAccessedAt,meta.Length));
            }catch(JsonException){
                Delete(bodyPath,metaPath);
            }
        }
        var total=0L;
        foreach(var e in entries.OrderByDescending(static e=>e.Accessed)){
            if(now-e.Accessed>maxUnused||total+e.Length>maxBytes){
                Delete(e.Body,e.Meta);
                continue;
            }
            total+=e.Length;
        }
    }

    /// <summary>最終利用時刻の更新は 1 時間に 1 回まで(読み取りのたびに書き込まないため)。規則は HttpCacheAccess。</summary>
    public static bool NeedsAccessUpdate(HttpCacheEntry entry,DateTimeOffset now){
        return HttpCacheAccess.NeedsUpdate(entry,now);
    }

    private (string Body,string Meta) Paths(string key){
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return (Path.Combine(this.directory,hash+".body"),Path.Combine(this.directory,hash+".meta"));
    }

    private static void Delete(string bodyPath,string metaPath){
        File.Delete(bodyPath);
        File.Delete(metaPath);
    }
}
