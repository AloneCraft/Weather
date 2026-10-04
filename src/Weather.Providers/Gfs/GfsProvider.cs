using System.Globalization;
using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Grib;
using Weather.Providers.Http;

namespace Weather.Providers.Gfs;

/// <summary>GFS から取り出す要素。</summary>
public enum GfsElement{WindU,WindV,Temperature,PrecipitationRate,CloudCover,Pressure}

/// <summary>.idx の 1 行(「番号:開始位置:d=初期時刻:要素:高度:予報時間:」)。End は次の行の開始位置 − 1(最後の行は null)。</summary>
internal sealed record GfsIndexEntry(int Number,long Offset,long? End,string Variable,string Level,string Forecast);

internal static class GfsIndex{
    public static IReadOnlyList<GfsIndexEntry> Parse(string text){
        var lines=text.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var parsed=new List<(int Number,long Offset,string[] Parts)>(lines.Length);
        foreach(var line in lines){
            var parts=line.Split(':');
            if(parts.Length<6||!int.TryParse(parts[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out var number)||!long.TryParse(parts[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out var offset)){
                throw new FormatException($"GFS の索引を解析できません: {line}");
            }
            parsed.Add((number,offset,parts));
        }
        var result=new List<GfsIndexEntry>(parsed.Count);
        for(var i=0;i<parsed.Count;i++){
            long? end=null;
            if(i+1<parsed.Count){
                end=parsed[i+1].Offset-1;
            }
            var p=parsed[i].Parts;
            result.Add(new GfsIndexEntry(parsed[i].Number,parsed[i].Offset,end,p[3],p[4],p[5]));
        }
        return result;
    }

    /// <summary>
    /// 要素の行を探す。降水強度と雲量は「n hour fcst」の瞬間値を使う(平均値は 6 時間ごとに区間が変わるため、時刻ごとに意味がそろわない)。
    /// </summary>
    public static GfsIndexEntry? Find(IReadOnlyList<GfsIndexEntry> entries,GfsElement element){
        foreach(var e in entries){
            var instant=!e.Forecast.Contains("ave",StringComparison.Ordinal)&&!e.Forecast.Contains("acc",StringComparison.Ordinal);
            var match=element switch{
                GfsElement.WindU=>e.Variable=="UGRD"&&e.Level=="10 m above ground",
                GfsElement.WindV=>e.Variable=="VGRD"&&e.Level=="10 m above ground",
                GfsElement.Temperature=>e.Variable=="TMP"&&e.Level=="2 m above ground",
                GfsElement.PrecipitationRate=>e.Variable=="PRATE"&&e.Level=="surface"&&instant,
                GfsElement.CloudCover=>e.Variable=="TCDC"&&e.Level=="entire atmosphere"&&instant,
                _=>e.Variable=="PRMSL"&&e.Level=="mean sea level",
            };
            if(match){
                return e;
            }
        }
        return null;
    }
}

public sealed record GfsOptions{
    /// <summary>格子の細かさ(0p50 = 0.5°。テストでは記録データの小さい 1p00 を使う)。</summary>
    public string Resolution{get;init;}="0p50";

    public string BaseUrl{get;init;}="https://"+HttpCachePolicy.GfsHost;
    public int MaxForecastHour{get;init;}=120;
    public int StepHours{get;init;}=3;
}

/// <summary>
/// NOAA GFS(AWS Open Data。パブリックドメイン、キー不要)。.idx で要素の位置を調べ、Range 要求で 1 要素だけ取得して復号する。
/// 日本周辺の格子点は NaN にして返す(方針 5。地図で GFS を日本周辺に描かない)。
/// </summary>
public sealed partial class GfsProvider(IHttpClientFactory httpClientFactory,IJapanArea japan,TimeProvider time,ILogger<GfsProvider> logger,GfsOptions options){
    public const string HttpClientName="gfs";
    private static readonly TimeSpan CycleCacheDuration=TimeSpan.FromMinutes(10);
    private readonly Lock gate=new();
    private (DateTimeOffset CheckedAt,DateTimeOffset Cycle)? latest;
    private (GridGeometry Geometry,bool[] Mask)? mask;

    /// <summary>
    /// 使える最新の実行回(00/06/12/18Z)。最後の予報時間の .idx があれば揃っているとみなし、なければ一つ前を見る。
    /// </summary>
    public async ValueTask<DateTimeOffset?> GetLatestCycleAsync(CancellationToken cancellationToken){
        var now=time.GetUtcNow();
        lock(this.gate){
            if(this.latest is {} cached&&now-cached.CheckedAt<CycleCacheDuration){
                return cached.Cycle;
            }
        }
        var candidate=new DateTimeOffset(now.UtcDateTime.Date,TimeSpan.Zero).AddHours(now.UtcDateTime.Hour/6*6);
        for(var i=0;i<4;i++){
            var cycle=candidate.AddHours(-6*i);
            try{
                await this.GetIndexAsync(cycle,options.MaxForecastHour,cancellationToken).ConfigureAwait(false);
                lock(this.gate){
                    this.latest=(now,cycle);
                }
                return cycle;
            }catch(WeatherProviderException ex) when(ex.Failure is ProviderFailure.NotFound or ProviderFailure.Forbidden){
                //まだ出ていない実行回(S3 は存在しないキーに 404 / 403 を返す)
            }
        }
        LogNoCycle(logger,candidate);
        return null;
    }

    /// <summary>実行回の有効時刻(StepHours ごと、MaxForecastHour まで。降水強度のない 0 時間は除く)。</summary>
    public IReadOnlyList<DateTimeOffset> ValidTimes(DateTimeOffset cycle){
        var list=new List<DateTimeOffset>();
        for(var h=options.StepHours;h<=options.MaxForecastHour;h+=options.StepHours){
            list.Add(cycle.AddHours(h));
        }
        return list;
    }

    public async ValueTask<GridField> GetFieldAsync(GfsElement element,DateTimeOffset cycle,int forecastHour,CancellationToken cancellationToken){
        var (index,_)=await this.GetIndexAsync(cycle,forecastHour,cancellationToken).ConfigureAwait(false);
        var entry=GfsIndex.Find(index,element)
            ??throw new WeatherProviderException(ProviderId.Gfs,ProviderFailure.InvalidResponse,$"GFS の索引に {element} がありません({cycle:yyyyMMddHH} f{forecastHour:000})。");
        var client=httpClientFactory.CreateClient(HttpClientName);
        var uri=this.FileUri(cycle,forecastHour);
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Gfs,uri,time,cancellationToken,(entry.Offset,entry.End)).ConfigureAwait(false);
        var message=ProviderHttp.Map(ProviderId.Gfs,uri,()=>Grib2Decoder.Decode(fetch.Body));
        var g=message.Grid;
        var geometry=new GridGeometry(g.Ni,g.Nj,Math.Max(g.La1,g.La2),g.Lo1,Math.Abs(g.Dj),Math.Abs(g.Di));
        var values=message.Values;
        var quantity=Convert(element,values);
        this.ApplyJapanMask(geometry,values);
        var source=new SourceAttribution{
            Provider=ProviderId.Gfs,
            AgencyName="NOAA",
            ProductName="GFS "+ResolutionLabel(options.Resolution),
            IssuedAt=cycle,
            RetrievedAt=fetch.RetrievedAt,
            License=License,
            SourceUrl=new Uri("https://registry.opendata.aws/noaa-gfs-bdp-pds/"),
            Processing=DataProcessing.Interpolated|DataProcessing.UnitConverted,
            IsStale=fetch.IsStale,
        };
        return new GridField(geometry,values,quantity,cycle.AddHours(forecastHour),cycle,source);
    }

    public static LicenseInfo License{get;}=new("Public domain (U.S. Government / NOAA)",new Uri("https://www.weather.gov/disclaimer"));

    private async ValueTask<(IReadOnlyList<GfsIndexEntry> Entries,FetchResult Fetch)> GetIndexAsync(DateTimeOffset cycle,int forecastHour,CancellationToken cancellationToken){
        var client=httpClientFactory.CreateClient(HttpClientName);
        var uri=new Uri(this.FileUri(cycle,forecastHour).AbsoluteUri+".idx");
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Gfs,uri,time,cancellationToken).ConfigureAwait(false);
        var entries=ProviderHttp.Map(ProviderId.Gfs,uri,()=>GfsIndex.Parse(System.Text.Encoding.ASCII.GetString(fetch.Body)));
        return (entries,fetch);
    }

    private Uri FileUri(DateTimeOffset cycle,int forecastHour){
        var c=cycle.UtcDateTime;
        return new Uri(string.Create(CultureInfo.InvariantCulture,$"{options.BaseUrl}/gfs.{c:yyyyMMdd}/{c:HH}/atmos/gfs.t{c:HH}z.pgrb2.{options.Resolution}.f{forecastHour:000}"));
    }

    /// <summary>GRIB の単位(K・Pa・kg/m²/s)を表示用の SI 系(℃・hPa・mm/h)にする。</summary>
    private static FieldQuantity Convert(GfsElement element,float[] values){
        switch(element){
            case GfsElement.WindU:
                return FieldQuantity.WindU;
            case GfsElement.WindV:
                return FieldQuantity.WindV;
            case GfsElement.Temperature:
                for(var i=0;i<values.Length;i++){
                    values[i]-=273.15f;
                }
                return FieldQuantity.TemperatureC;
            case GfsElement.PrecipitationRate:
                for(var i=0;i<values.Length;i++){
                    values[i]=Math.Max(0,values[i]*3600f);
                }
                return FieldQuantity.PrecipitationMmPerHour;
            case GfsElement.CloudCover:
                return FieldQuantity.CloudCoverPercent;
            default:
                for(var i=0;i<values.Length;i++){
                    values[i]/=100f;
                }
                return FieldQuantity.PressureHpa;
        }
    }

    /// <summary>日本周辺の格子点を NaN にする。マスクは格子の形ごとに一度だけ作る。</summary>
    private void ApplyJapanMask(GridGeometry geometry,float[] values){
        bool[] masked;
        lock(this.gate){
            if(this.mask is {} m&&m.Geometry==geometry){
                masked=m.Mask;
            }else{
                masked=new bool[geometry.Count];
                for(var r=0;r<geometry.Rows;r++){
                    var lat=geometry.LatitudeOf(r);
                    for(var c=0;c<geometry.Columns;c++){
                        var lon=geometry.LongitudeOf(c);
                        if(lon>180){
                            lon-=360;
                        }
                        masked[r*geometry.Columns+c]=japan.Contains(lat,lon);
                    }
                }
                this.mask=(geometry,masked);
            }
        }
        for(var i=0;i<values.Length;i++){
            if(masked[i]){
                values[i]=float.NaN;
            }
        }
    }

    private static string ResolutionLabel(string resolution){
        switch(resolution){
            case "0p25":
                return "0.25°";
            case "0p50":
                return "0.5°";
            default:
                return "1°";
        }
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="GFS の実行回が見つかりません(最新の候補 {Candidate})")]
    private static partial void LogNoCycle(ILogger logger,DateTimeOffset candidate);
}
