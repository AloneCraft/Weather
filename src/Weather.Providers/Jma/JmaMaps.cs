using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Weather.Core;
using Weather.Providers.Conditions;

namespace Weather.Providers.Jma;

/// <summary>jmatile の targetTimes の 1 件(時刻は UTC)。</summary>
internal sealed record JmaTargetTime(DateTimeOffset BaseTime,DateTimeOffset ValidTime,IReadOnlyList<string> Elements);

/// <summary>
/// 海上分布予報の風速の 1 階級(GeoJSON の多角形)。リングは(経度, 緯度)の列で、穴のリングも含む(偶奇規則で判定する)。
/// SpeedMs は階級の代表値(下限と上限の中央。上限がない階級は下限 + 5 kt)を m/s にしたもの。
/// </summary>
internal sealed record MarineSpeedArea(IReadOnlyList<(double Lon,double Lat)[]> Rings,double SpeedMs,string Level){
    public bool Contains(double latitude,double longitude){
        var inside=false;
        foreach(var ring in this.Rings){
            for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){
                var (xi,yi)=ring[i];
                var (xj,yj)=ring[j];
                if((yi>latitude)!=(yj>latitude)&&longitude<(xj-xi)*(latitude-yi)/(yj-yi)+xi){
                    inside=!inside;
                }
            }
        }
        return inside;
    }
}

/// <summary>気象庁の地図タイルの時刻一覧(各プロダクト。有効時刻の昇順)。</summary>
internal sealed record JmaTileTimes(
    IReadOnlyList<JmaTargetTime> Nowcast,
    IReadOnlyList<JmaTargetTime> RainForecast,
    IReadOnlyList<JmaTargetTime> Distribution,
    IReadOnlyList<JmaTargetTime> Marine){
    public static JmaTileTimes Empty{get;}=new([],[],[],[]);
}

/// <summary>
/// 気象庁の地図タイル(jmatile)。URL の形・ズーム・凡例は 2026-10-04 に jmatile の設定(table/*.properties.xml)から確認した。
/// - 雨雲の動き(nowc): targetTimes_N1(解析。過去 3 時間・5 分ごと)と N2(予測。1 時間先まで)。要素 hrpns、偶数ズーム 4〜10
/// - 今後の雨(rasrf): 有効時刻ごとに最新の初期時刻を使う。要素 rasrf、偶数ズーム 4〜10
/// - 天気分布予報(wdist): 要素 temp・wm・r3、偶数ズーム 4〜10、3 時間ごと
/// - 海上分布予報(umimesh): 風速 ws(ズーム 4〜8 のタイルと GeoJSON の階級の多角形)、風向 wd(GeoJSON。0.5° の点、8 方位)、6 時間ごと 24 時間先まで
/// </summary>
internal static class JmaMaps{
    public const string Root=JmaTileLayer.Root;

    /// <summary>1 ノット(m/s)。</summary>
    public const double KnotMs=1852.0/3600.0;

    /// <summary>海上分布予報の風向の点の間隔(度)。</summary>
    public const double MarineStep=0.5;

    public static readonly Uri NowcastPage=new("https://www.jma.go.jp/bosai/nowc/");
    public static readonly Uri RainForecastPage=new("https://www.jma.go.jp/bosai/kaikotan/");
    public static readonly Uri DistributionPage=new("https://www.jma.go.jp/bosai/wdist/");
    public static readonly Uri MarinePage=new("https://www.jma.go.jp/bosai/umimesh/");
    public static readonly Uri AmedasPage=new("https://www.jma.go.jp/bosai/amedas/");

    public static IReadOnlyList<JmaTargetTime> ParseTargetTimes(JsonElement root){
        var list=new List<JmaTargetTime>();
        foreach(var item in root.EnumerateArray()){
            var elements=new List<string>();
            if(item.TryGetProperty("elements",out var e)&&e.ValueKind==JsonValueKind.Array){
                foreach(var name in e.EnumerateArray()){
                    elements.Add(name.GetString()??"");
                }
            }
            list.Add(new JmaTargetTime(ParseTime(item.GetProperty("basetime").GetString()),ParseTime(item.GetProperty("validtime").GetString()),elements));
        }
        return list;
    }

    /// <summary>「yyyyMMddHHmmss」(UTC)。</summary>
    public static DateTimeOffset ParseTime(string? text){
        return DateTimeOffset.ParseExact(text??"","yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>有効時刻ごとに最新の初期時刻だけを残し、有効時刻の昇順に並べる。</summary>
    public static IReadOnlyList<JmaTargetTime> LatestPerValidTime(IEnumerable<JmaTargetTime> times){
        return [..times.GroupBy(static t=>t.ValidTime).Select(static g=>g.MaxBy(static t=>t.BaseTime)!).OrderBy(static t=>t.ValidTime)];
    }

    /// <summary>許容差の中で最も近い時刻。要素の指定があれば、その要素を含む時刻だけを候補にする。</summary>
    public static JmaTargetTime? Nearest(IReadOnlyList<JmaTargetTime> times,DateTimeOffset at,TimeSpan tolerance,string? element=null){
        JmaTargetTime? best=null;
        var bestDistance=TimeSpan.MaxValue;
        foreach(var t in times){
            if(element is not null&&t.Elements.Count>0&&!t.Elements.Contains(element,StringComparer.Ordinal)){
                continue;
            }
            var d=(t.ValidTime-at).Duration();
            if(d<=tolerance&&d<bestDistance){
                best=t;
                bestDistance=d;
            }
        }
        return best;
    }

    public static SourceAttribution Source(string product,DateTimeOffset issuedAt,DateTimeOffset retrievedAt,bool stale,Uri page){
        return new SourceAttribution{
            Provider=ProviderId.Jma,
            AgencyName="気象庁",
            ProductName=product,
            IssuedAt=issuedAt,
            RetrievedAt=retrievedAt,
            License=JmaAttribution.License,
            SourceUrl=page,
            IsStale=stale,
        };
    }

    /// <summary>海上分布予報の風向(GeoJSON の点。windDir は 8 方位で、風が吹いてくる向き)。</summary>
    public static IReadOnlyList<WindArrow> ParseMarineWind(byte[] body){
        using var doc=JsonDocument.Parse(Decompress(body));
        var list=new List<WindArrow>();
        foreach(var feature in doc.RootElement.GetProperty("features").EnumerateArray()){
            var geometry=feature.GetProperty("geometry");
            if(geometry.GetProperty("type").GetString()!="Point"){
                continue;
            }
            var coordinates=geometry.GetProperty("coordinates");
            string? direction=null;
            if(feature.GetProperty("properties").TryGetProperty("windDir",out var d)){
                direction=d.GetString();
            }
            if(EightPoint(direction) is not {} degrees){
                continue;
            }
            list.Add(new WindArrow(new GeoPoint(coordinates[1].GetDouble(),coordinates[0].GetDouble()),degrees,null));
        }
        return list;
    }

    /// <summary>
    /// 海上分布予報の風速の階級(GeoJSON の多角形。level は「0&lt;=kt&lt;25」「25&lt;=kt&lt;30」…「65&lt;=kt」)。
    /// 「NoData」(陸地など)と読めない階級は除く。
    /// </summary>
    public static IReadOnlyList<MarineSpeedArea> ParseMarineSpeed(byte[] body){
        using var doc=JsonDocument.Parse(Decompress(body));
        var list=new List<MarineSpeedArea>();
        foreach(var feature in doc.RootElement.GetProperty("features").EnumerateArray()){
            string? level=null;
            if(feature.GetProperty("properties").TryGetProperty("level",out var l)){
                level=l.GetString();
            }
            if(level is null||RepresentativeKnots(level) is not {} knots){
                continue;
            }
            var geometry=feature.GetProperty("geometry");
            var coordinates=geometry.GetProperty("coordinates");
            var rings=new List<(double Lon,double Lat)[]>();
            switch(geometry.GetProperty("type").GetString()){
                case "Polygon":
                    AddRings(coordinates,rings);
                    break;
                case "MultiPolygon":
                    foreach(var polygon in coordinates.EnumerateArray()){
                        AddRings(polygon,rings);
                    }
                    break;
            }
            if(rings.Count>0){
                list.Add(new MarineSpeedArea(rings,knots*KnotMs,level));
            }
        }
        return list;
    }

    /// <summary>階級の代表値(kt)。「A&lt;=kt&lt;B」は (A+B)/2、「A&lt;=kt」は A+5。読めなければ null。</summary>
    public static double? RepresentativeKnots(string level){
        var index=level.IndexOf("kt",StringComparison.Ordinal);
        if(index<0){
            return null;
        }
        var left=level[..index];
        var right=level[(index+2)..];
        double? lower=null;
        double? upper=null;
        if(left.EndsWith("<=",StringComparison.Ordinal)&&double.TryParse(left[..^2],NumberStyles.Float,CultureInfo.InvariantCulture,out var a)){
            lower=a;
        }
        if(right.StartsWith('<')&&double.TryParse(right[1..],NumberStyles.Float,CultureInfo.InvariantCulture,out var b)){
            upper=b;
        }
        if(lower is {} lo&&upper is {} hi){
            return (lo+hi)/2;
        }
        if(lower is {} open){
            return open+5;
        }
        return null;
    }

    /// <summary>
    /// 海上分布予報の風向(8 方位の点)と風速の階級から、海上の風の格子(0.5°、U・V)を作る(地図の海上の風の流れ用)。
    /// 風速の階級が分からない点(NoData・範囲外)と陸上は NaN。点がなければ null。
    /// 階級の代表値と 8 方位からの補間は加工にあたるため、出典の Processing に Interpolated | UnitConverted を付けて渡す。
    /// </summary>
    public static WindField? MarineWindField(IReadOnlyList<WindArrow> directions,IReadOnlyList<MarineSpeedArea> speeds,DateTimeOffset validTime,DateTimeOffset referenceTime,SourceAttribution source){
        ArgumentNullException.ThrowIfNull(directions);
        ArgumentNullException.ThrowIfNull(speeds);
        if(directions.Count==0||speeds.Count==0){
            return null;
        }
        var north=directions.Max(static a=>a.Point.Latitude);
        var south=directions.Min(static a=>a.Point.Latitude);
        var west=directions.Min(static a=>a.Point.Longitude);
        var east=directions.Max(static a=>a.Point.Longitude);
        var columns=Math.Max(2,(int)Math.Round((east-west)/MarineStep)+1);
        var rows=Math.Max(2,(int)Math.Round((north-south)/MarineStep)+1);
        var geometry=new GridGeometry(columns,rows,north,west,MarineStep,MarineStep);
        var u=new float[geometry.Count];
        var v=new float[geometry.Count];
        Array.Fill(u,float.NaN);
        Array.Fill(v,float.NaN);
        foreach(var arrow in directions){
            var area=speeds.FirstOrDefault(s=>s.Contains(arrow.Point.Latitude,arrow.Point.Longitude));
            if(area is null){
                continue;
            }
            var column=(int)Math.Round((arrow.Point.Longitude-west)/MarineStep);
            var row=(int)Math.Round((north-arrow.Point.Latitude)/MarineStep);
            var index=row*columns+column;
            //吹いてくる向き θ の風は θ+180° へ流れる: U = -s·sinθ、V = -s·cosθ
            var radians=arrow.FromDirectionDeg*Math.PI/180;
            u[index]=(float)(-area.SpeedMs*Math.Sin(radians));
            v[index]=(float)(-area.SpeedMs*Math.Cos(radians));
        }
        return new WindField(
            new GridField(geometry,u,FieldQuantity.WindU,validTime,referenceTime,source),
            new GridField(geometry,v,FieldQuantity.WindV,validTime,referenceTime,source));
    }

    private static void AddRings(JsonElement polygon,List<(double Lon,double Lat)[]> rings){
        foreach(var ring in polygon.EnumerateArray()){
            var points=new List<(double Lon,double Lat)>();
            foreach(var p in ring.EnumerateArray()){
                points.Add((p[0].GetDouble(),p[1].GetDouble()));
            }
            if(points.Count>=3){
                rings.Add([..points]);
            }
        }
    }

    /// <summary>サーバーは gzip のまま返すことがある(Content-Encoding なし)。</summary>
    private static byte[] Decompress(byte[] body){
        if(body.Length>2&&body[0]==0x1f&&body[1]==0x8b){
            using var input=new GZipStream(new MemoryStream(body),CompressionMode.Decompress);
            using var output=new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
        return body;
    }

    private static double? EightPoint(string? direction){
        switch(direction){
            case "N":
                return 0;
            case "NE":
                return 45;
            case "E":
                return 90;
            case "SE":
                return 135;
            case "S":
                return 180;
            case "SW":
                return 225;
            case "W":
                return 270;
            case "NW":
                return 315;
            default:
                return null;
        }
    }

    /// <summary>アメダスの全地点の最新値の URL(時刻は日本時間の yyyyMMddHHmm00)。</summary>
    public static Uri AmedasMapUri(DateTimeOffset time){
        var jst=time.ToOffset(JmaAttribution.Jst);
        return new Uri(string.Create(CultureInfo.InvariantCulture,$"https://www.jma.go.jp/bosai/amedas/data/map/{jst:yyyyMMddHHmm}00.json"));
    }

    /// <summary>
    /// アメダスの全地点の値(風・気温)。品質フラグが 0(正常)の値だけを使う(History.md と同じ扱い)。
    /// 観測所の位置は amedastable(JmaCatalog)から引く。
    /// </summary>
    public static (IReadOnlyList<WindArrow> Wind,IReadOnlyList<PointValue> Temperature) ParseAmedasMap(JsonElement root,JmaCatalog catalog){
        var wind=new List<WindArrow>();
        var temperature=new List<PointValue>();
        foreach(var station in root.EnumerateObject()){
            if(catalog.FindStation(station.Name) is not {} s){
                continue;
            }
            var point=new GeoPoint(s.Latitude,s.Longitude);
            if(Value(station.Value,"temp") is {} t){
                temperature.Add(new PointValue(point,t));
            }
            if(Value(station.Value,"wind") is {} speed&&Value(station.Value,"windDirection") is {} code){
                var direction=Compass.FromAmedasCode((int)code);
                if(direction is {} degrees&&speed>0){
                    wind.Add(new WindArrow(point,degrees,speed));
                }
            }
        }
        return (wind,temperature);
    }

    private static double? Value(JsonElement station,string name){
        if(!station.TryGetProperty(name,out var pair)||pair.ValueKind!=JsonValueKind.Array||pair.GetArrayLength()<2){
            return null;
        }
        if(pair[0].ValueKind!=JsonValueKind.Number||pair[1].ValueKind!=JsonValueKind.Number||pair[1].GetInt32()!=0){
            return null;
        }
        return pair[0].GetDouble();
    }

    public static bool IsNotFound(WeatherProviderException ex){
        return ex.Failure is ProviderFailure.NotFound or ProviderFailure.Forbidden;
    }
}
