using System.Net.Http.Headers;
using Weather.Core;

namespace Weather.Providers.Http;

/// <summary>RFC 9111 を簡略化した鮮度計算(私的キャッシュのため s-maxage は無視する)。</summary>
public static class HttpFreshness{
    public static TimeSpan Lifetime(HttpCacheEntry entry){
        ArgumentNullException.ThrowIfNull(entry);
        if(entry.MaxAge is {} maxAge){
            return maxAge;
        }
        if(entry.Expires is {} expires){
            var baseTime=entry.ReceivedAt;
            if(entry.Date is {} date){
                baseTime=date;
            }
            var lifetime=expires-baseTime;
            if(lifetime<TimeSpan.Zero){
                return TimeSpan.Zero;
            }
            return lifetime;
        }
        return TimeSpan.Zero;
    }

    public static TimeSpan CurrentAge(HttpCacheEntry entry,DateTimeOffset now){
        ArgumentNullException.ThrowIfNull(entry);
        var age=now-entry.ReceivedAt;
        if(entry.Age is {} headerAge){
            age+=headerAge;
        }
        if(age<TimeSpan.Zero){
            return TimeSpan.Zero;
        }
        return age;
    }

    public static bool IsFresh(HttpCacheEntry entry,DateTimeOffset now,TimeSpan minimumFreshness){
        var lifetime=Lifetime(entry);
        if(minimumFreshness>lifetime){
            lifetime=minimumFreshness;
        }
        return CurrentAge(entry,now)<lifetime;
    }

    public static bool CanServeStale(HttpCacheEntry? entry,DateTimeOffset now,TimeSpan maxStale){
        if(entry is null){
            return false;
        }
        if(maxStale==TimeSpan.MaxValue){
            return true;
        }
        return now-entry.ReceivedAt<=maxStale;
    }

    public static bool IsStorable(HttpResponseMessage response){
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.CacheControl?.NoStore!=true;
    }

    public static HttpCacheEntry CreateEntry(string key,HttpResponseMessage response,byte[] body,DateTimeOffset now){
        ArgumentNullException.ThrowIfNull(response);
        return new HttpCacheEntry{
            Key=key,
            StatusCode=(int)response.StatusCode,
            Body=body,
            ContentType=response.Content.Headers.ContentType?.ToString(),
            ETag=response.Headers.ETag?.ToString(),
            LastModified=RawHeader(response.Content.Headers,"Last-Modified"),
            Date=response.Headers.Date,
            Age=response.Headers.Age,
            MaxAge=response.Headers.CacheControl?.MaxAge,
            Expires=response.Content.Headers.Expires,
            ReceivedAt=now,
            LastAccessedAt=now,
        };
    }

    /// <summary>304 応答でメタデータを更新する。本文は保持したまま。</summary>
    public static HttpCacheEntry Revalidate(HttpCacheEntry entry,HttpResponseMessage notModified,DateTimeOffset now){
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(notModified);
        var etag=entry.ETag;
        if(notModified.Headers.ETag is {} newEtag){
            etag=newEtag.ToString();
        }
        var maxAge=entry.MaxAge;
        if(notModified.Headers.CacheControl?.MaxAge is {} newMaxAge){
            maxAge=newMaxAge;
        }
        var expires=entry.Expires;
        if(notModified.Content?.Headers.Expires is {} newExpires){
            expires=newExpires;
        }
        var lastModified=entry.LastModified;
        if(notModified.Content is not null&&RawHeader(notModified.Content.Headers,"Last-Modified") is {} newLastModified){
            lastModified=newLastModified;
        }
        return entry with{
            ETag=etag,
            MaxAge=maxAge,
            Expires=expires,
            LastModified=lastModified,
            Date=notModified.Headers.Date??entry.Date,
            Age=notModified.Headers.Age,
            ReceivedAt=now,
            LastAccessedAt=now,
        };
    }

    private static string? RawHeader(HttpHeaders headers,string name){
        if(headers.TryGetValues(name,out var values)){
            foreach(var value in values){
                return value;
            }
        }
        return null;
    }
}
