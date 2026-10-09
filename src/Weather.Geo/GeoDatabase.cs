using Weather.Core;
using Weather.Geo.Data;
using Weather.Geo.Spatial;

namespace Weather.Geo;

/// <summary>埋め込みリソースの地理データと索引。読み込みは初回利用時(遅延)。</summary>
public sealed class GeoDatabase{
    private const double KmPerDegree=111;
    private readonly Lazy<IReadOnlyList<JmaAreaRecord>> areas;
    private readonly Lazy<IReadOnlyList<CountryRecord>> countries;
    private readonly Lazy<IReadOnlyList<PlaceRecord>> places;
    private readonly Lazy<JapanAreaMask> mask;
    private readonly Lazy<Dictionary<(int,int),List<PlaceRecord>>> placeBuckets;
    private readonly Lazy<Dictionary<string,CountryRecord>> countryByIso;

    public GeoDatabase(Func<IReadOnlyList<JmaAreaRecord>> areas,Func<IReadOnlyList<CountryRecord>> countries,Func<IReadOnlyList<PlaceRecord>> places,Func<JapanAreaMask> mask){
        this.areas=new Lazy<IReadOnlyList<JmaAreaRecord>>(areas);
        this.countries=new Lazy<IReadOnlyList<CountryRecord>>(countries);
        this.places=new Lazy<IReadOnlyList<PlaceRecord>>(places);
        this.mask=new Lazy<JapanAreaMask>(mask);
        this.placeBuckets=new Lazy<Dictionary<(int,int),List<PlaceRecord>>>(this.BuildBuckets);
        this.countryByIso=new Lazy<Dictionary<string,CountryRecord>>(()=>{
            var map=new Dictionary<string,CountryRecord>(StringComparer.Ordinal);
            foreach(var country in this.Countries){
                map.TryAdd(country.Iso2,country);
            }
            return map;
        });
    }

    public static GeoDatabase LoadEmbedded(){
        return new GeoDatabase(
            static ()=>GeoDataFormat.ReadJmaAreas(Open("jp-areas.bin")),
            static ()=>GeoDataFormat.ReadCountries(Open("countries.bin")),
            static ()=>GeoDataFormat.ReadPlaces(Open("places.bin")),
            static ()=>GeoDataFormat.ReadMask(Open("jp-mask.bin")));
    }

    public IReadOnlyList<JmaAreaRecord> JmaAreas=>this.areas.Value;
    public IReadOnlyList<CountryRecord> Countries=>this.countries.Value;
    public IReadOnlyList<PlaceRecord> Places=>this.places.Value;
    public JapanAreaMask JapanMask=>this.mask.Value;

    public CountryRecord? FindCountryByIso(string iso2){
        this.countryByIso.Value.TryGetValue(iso2,out var country);
        return country;
    }

    public JmaAreaRecord? FindJmaAreaContaining(double lat,double lon){
        foreach(var area in this.JmaAreas){
            if(area.Shape.BoxContains(lat,lon)&&Polygons.Contains(area.Shape,lat,lon)){
                return area;
            }
        }
        return null;
    }

    public (JmaAreaRecord? Area,double DistanceKm) FindNearestJmaArea(double lat,double lon,double searchKm){
        JmaAreaRecord? best=null;
        var bestDistance=double.MaxValue;
        foreach(var area in this.JmaAreas){
            if(Polygons.BoxDistanceKm(area.Shape,lat,lon)>searchKm){
                continue;
            }
            var d=Polygons.DistanceKm(area.Shape,lat,lon);
            if(d<bestDistance){
                bestDistance=d;
                best=area;
            }
        }
        return (best,bestDistance);
    }

    public CountryRecord? FindCountry(double lat,double lon){
        foreach(var country in this.Countries){
            if(country.Shape.BoxContains(lat,lon)&&Polygons.Contains(country.Shape,lat,lon)){
                return country;
            }
        }
        return null;
    }

    /// <summary>最寄りの都市。country を指定すると、その国の都市だけを候補にする(国境の向こうの都市を名前・タイムゾーンに使わない)。</summary>
    public PlaceRecord? FindNearestPlace(double lat,double lon,double maxKm,string? country=null){
        var buckets=this.placeBuckets.Value;
        var latRange=(int)Math.Ceiling(maxKm/100)+1;
        var lonRange=LongitudeRange(lat,maxKm,latRange);
        var bLat=(int)Math.Floor(lat);
        var bLon=(int)Math.Floor(lon);
        PlaceRecord? best=null;
        var bestDistance=maxKm;
        for(var dy=-latRange;dy<=latRange;dy++){
            for(var dx=-lonRange;dx<=lonRange;dx++){
                var key=(bLat+dy,Wrap(bLon+dx));
                if(!buckets.TryGetValue(key,out var list)){
                    continue;
                }
                foreach(var place in list){
                    if(country is not null&&place.Country!=country){
                        continue;
                    }
                    var d=GeoMath.HaversineKm(lat,lon,place.Latitude,place.Longitude);
                    if(d<=bestDistance){
                        bestDistance=d;
                        best=place;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>経度 1° の距離は緯度とともに縮むので、探索円が届く最も高い緯度での長さから、東西に調べる区画数を決める。極に届くときは全周。</summary>
    private static int LongitudeRange(double lat,double maxKm,int latRange){
        var farthestLat=Math.Abs(lat)+maxKm/KmPerDegree;
        if(farthestLat>=89){
            return 180;
        }
        var degrees=maxKm/(KmPerDegree*Math.Cos(farthestLat*Math.PI/180));
        return Math.Min(180,Math.Max(latRange,(int)Math.Ceiling(degrees)+1));
    }

    private Dictionary<(int,int),List<PlaceRecord>> BuildBuckets(){
        var buckets=new Dictionary<(int,int),List<PlaceRecord>>();
        foreach(var place in this.Places){
            var key=((int)Math.Floor(place.Latitude),(int)Math.Floor(place.Longitude));
            if(!buckets.TryGetValue(key,out var list)){
                list=[];
                buckets[key]=list;
            }
            list.Add(place);
        }
        return buckets;
    }

    private static int Wrap(int lon){
        if(lon<-180){
            return lon+360;
        }
        if(lon>=180){
            return lon-360;
        }
        return lon;
    }

    private static Stream Open(string name){
        return typeof(GeoDatabase).Assembly.GetManifestResourceStream("Weather.Geo.Data."+name)
            ??throw new InvalidOperationException($"地理データがありません: {name}(tools/Weather.GeoDataBuilder で生成してください)");
    }
}
