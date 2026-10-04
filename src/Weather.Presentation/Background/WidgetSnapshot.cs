using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation.Background;

public enum WidgetState{Ready,OutOfCoverage,NoPlace,NotAvailable}

/// <summary>
/// ウィジェットに表示する内容。Android の AppWidget と iOS の WidgetKit 拡張(Swift)が同じ JSON を読む。
/// 文字列は書式済み(単位・言語の処理を各 OS の実装に持たせない)。出典は必ず含める(方針 7)。
/// </summary>
public sealed record WidgetSnapshot{
    public required WidgetState State{get;init;}
    public required string PlaceName{get;init;}
    public string Temperature{get;init;}="--";
    public string Icon{get;init;}="";
    public string ConditionText{get;init;}="";
    public string? HighLow{get;init;}
    public string? Precipitation{get;init;}
    public string? AlertText{get;init;}
    public string Attribution{get;init;}="";
    public string Credit{get;init;}="";
    public string UpdatedText{get;init;}="";
    public required DateTimeOffset GeneratedAt{get;init;}
    public bool IsStale{get;init;}

    /// <summary>背景で更新されるか。MET の地点は規約で背景取得できないため false(アプリを開いたときだけ更新)。</summary>
    public bool BackgroundRefresh{get;init;}

    public string? Note{get;init;}
}

public static class WidgetSnapshotFactory{
    public static WidgetSnapshot Create(string placeName,ForecastResult? forecast,AlertResult? alerts,bool backgroundRefresh,UnitSystem units,DateTimeOffset now){
        ArgumentNullException.ThrowIfNull(placeName);
        string? note=null;
        if(!backgroundRefresh){
            note=Strings.WidgetForegroundOnly;
        }
        if(forecast?.Availability==Availability.JapanOutOfCoverage){
            return new WidgetSnapshot{State=WidgetState.OutOfCoverage,PlaceName=placeName,ConditionText=Strings.OutOfCoverage,GeneratedAt=now,BackgroundRefresh=backgroundRefresh};
        }
        if(forecast?.Forecast is not {} f){
            return new WidgetSnapshot{State=WidgetState.NotAvailable,PlaceName=placeName,ConditionText=Strings.WidgetNotAvailable,GeneratedAt=now,BackgroundRefresh=backgroundRefresh,Note=note};
        }
        var zone=TimeText.Zone(f.Location.TimeZoneId);
        var current=CurrentConditions.From(f,f.Location.Point,now,zone,units);
        string? highLow=null;
        string? precipitation=null;
        if(current.Today is {} today){
            highLow=string.Format(CultureInfo.CurrentCulture,Strings.WidgetHighLowFormat,Units.Temperature(today.TempMaxC,units),Units.Temperature(today.TempMinC,units));
            if(CurrentConditions.DayProbability(today) is {} pop){
                precipitation=string.Format(CultureInfo.CurrentCulture,Strings.WidgetPrecipitationFormat,Units.Probability(pop));
            }
        }
        var primary=f.Daily.Select(static d=>d.Source).FirstOrDefault()??f.TimeSeries.Select(static p=>p.Source).FirstOrDefault();
        var attribution="";
        var credit="";
        var updated="";
        if(primary is not null){
            attribution=AttributionText.Short(primary,zone);
            credit=AttributionText.Credit(primary);
            updated=string.Format(CultureInfo.CurrentCulture,Strings.WidgetRetrievedFormat,TimeText.Clock(primary.RetrievedAt,zone));
        }
        return new WidgetSnapshot{
            State=WidgetState.Ready,
            PlaceName=placeName,
            Temperature=current.Temperature,
            Icon=current.Icon,
            ConditionText=current.Text,
            HighLow=highLow,
            Precipitation=precipitation,
            AlertText=AlertText(alerts),
            Attribution=attribution,
            Credit=credit,
            UpdatedText=updated,
            GeneratedAt=now,
            IsStale=f.IsStale,
            BackgroundRefresh=backgroundRefresh,
            Note=note,
        };
    }

    public static WidgetSnapshot NoPlace(DateTimeOffset now){
        return new WidgetSnapshot{State=WidgetState.NoPlace,PlaceName="",ConditionText=Strings.WidgetNoPlace,GeneratedAt=now};
    }

    /// <summary>発表中の警報の名称(機関の表現のまま。重い順に最大 2 件)。</summary>
    private static string? AlertText(AlertResult? alerts){
        if(alerts?.Alerts is not {} set){
            return null;
        }
        var names=set.Active.OrderByDescending(static a=>a.Tier).Select(static a=>a.EventName).Distinct(StringComparer.Ordinal).ToList();
        if(names.Count==0){
            return null;
        }
        var text=string.Join(" ",names.Take(2));
        if(names.Count>2){
            text+=Strings.AndMore;
        }
        return text;
    }
}

/// <summary>ウィジェットの JSON(source generator。リフレクションを使わない)。名前は camelCase、列挙は文字列。</summary>
public static class WidgetSnapshotJson{
    public const string FileName="widget.json";

    public static string Serialize(WidgetSnapshot snapshot){
        return JsonSerializer.Serialize(snapshot,WidgetJsonContext.Default.WidgetSnapshot);
    }

    public static WidgetSnapshot? Deserialize(string json){
        try{
            return JsonSerializer.Deserialize(json,WidgetJsonContext.Default.WidgetSnapshot);
        }catch(JsonException){
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(WidgetSnapshot))]
internal sealed partial class WidgetJsonContext:JsonSerializerContext;
