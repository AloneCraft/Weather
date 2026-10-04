using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weather.Core;

namespace Weather.Geo;

/// <summary>検索用の正規化(NFKC、小文字化、カタカナ → ひらがな、ヶ・ヵ の統合、空白除去)。</summary>
public static class TextNormalizer{
    public static string Normalize(string? text){
        if(string.IsNullOrWhiteSpace(text)){
            return "";
        }
        var normalized=text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder=new StringBuilder(normalized.Length);
        foreach(var c in normalized){
            if(char.IsWhiteSpace(c)){
                continue;
            }
            //小書きの「ヶ」「ヵ」は「ケ」「カ」と同じ扱いにする(茅ヶ崎市と茅ケ崎のように地名で混在する)
            if(c=='ヶ'){
                builder.Append('け');
            }else if(c=='ヵ'){
                builder.Append('か');
            }else if(c>='ァ'&&c<='ヶ'){
                builder.Append((char)(c-0x60));
            }else{
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    public static bool ContainsJapanese(string text){
        foreach(var c in text){
            if((c>='぀'&&c<='ヿ')||(c>='一'&&c<='鿿')){
                return true;
            }
        }
        return false;
    }
}

/// <summary>オフラインの地点検索(CacheAndRouting.md「地点検索」)。</summary>
public sealed class PlaceSearch:IPlaceSearch{
    private readonly GeoDatabase database;
    private readonly Lazy<List<Entry>> index;

    private sealed record Entry(string[] Keys,PlaceSuggestion Suggestion);

    public PlaceSearch(GeoDatabase database){
        this.database=database;
        this.index=new Lazy<List<Entry>>(this.Build);
    }

    public ValueTask<IReadOnlyList<PlaceSuggestion>> SearchAsync(string query,int maxCount,CancellationToken cancellationToken){
        return ValueTask.FromResult(this.Search(query,maxCount));
    }

    public IReadOnlyList<PlaceSuggestion> Search(string query,int maxCount){
        var q=TextNormalizer.Normalize(query);
        if(q.Length==0){
            return [];
        }
        var japanese=TextNormalizer.ContainsJapanese(query);
        var results=new List<(int Score,Entry Entry)>();
        foreach(var entry in this.index.Value){
            var score=0;
            foreach(var key in entry.Keys){
                if(key.Length==0){
                    continue;
                }
                if(key==q){
                    score=Math.Max(score,3);
                }else if(key.StartsWith(q,StringComparison.Ordinal)){
                    score=Math.Max(score,2);
                }else if(q.Length>=2&&key.Contains(q,StringComparison.Ordinal)){
                    score=Math.Max(score,1);
                }
            }
            if(score>0){
                results.Add((score,entry));
            }
        }
        return [..results
            .OrderByDescending(static r=>r.Score)
            .ThenBy(r=>KindOrder(r.Entry.Suggestion.Kind,japanese))
            .ThenByDescending(static r=>r.Entry.Suggestion.Population)
            .Take(maxCount)
            .Select(static r=>r.Entry.Suggestion)];
    }

    private static int KindOrder(PlaceKind kind,bool japaneseQuery){
        if(japaneseQuery){
            if(kind==PlaceKind.JmaArea){
                return 0;
            }
            return 1;
        }
        if(kind==PlaceKind.City){
            return 0;
        }
        return 1;
    }

    private List<Entry> Build(){
        var list=new List<Entry>();
        foreach(var area in this.database.JmaAreas){
            list.Add(new Entry(
                [TextNormalizer.Normalize(area.Name),TextNormalizer.Normalize(area.Kana),TextNormalizer.Normalize(area.EnglishName)],
                new PlaceSuggestion("jma:"+area.Code,area.Name,area.PrefectureName,new GeoPoint(area.LabelLatitude,area.LabelLongitude),"JP",PlaceKind.JmaArea,0)));
        }
        foreach(var place in this.database.Places){
            var countryName=this.database.FindCountryByIso(place.Country)?.NameJa??place.Country;
            var detail=countryName;
            if(!string.IsNullOrEmpty(place.Admin1)){
                detail=place.Admin1+"、"+countryName;
            }
            list.Add(new Entry(
                [TextNormalizer.Normalize(place.Name),TextNormalizer.Normalize(place.AsciiName),TextNormalizer.Normalize(place.JaName)],
                new PlaceSuggestion("gn:"+place.Id.ToString(CultureInfo.InvariantCulture),place.JaName??place.Name,detail,new GeoPoint(place.Latitude,place.Longitude),place.Country,PlaceKind.City,place.Population)));
        }
        return list;
    }
}

public static class GeoServiceCollectionExtensions{
    /// <summary>地点の解決・検索を登録する(MAUI と Functions で共通)。</summary>
    public static IServiceCollection AddWeatherGeo(this IServiceCollection services){
        services.TryAddSingleton(static _=>GeoDatabase.LoadEmbedded());
        services.TryAddSingleton<LocationResolver>();
        services.TryAddSingleton<ILocationResolver>(static sp=>sp.GetRequiredService<LocationResolver>());
        services.TryAddSingleton<PlaceSearch>();
        services.TryAddSingleton<IPlaceSearch>(static sp=>sp.GetRequiredService<PlaceSearch>());
        services.TryAddSingleton<IJapanArea,JapanArea>();
        return services;
    }
}
