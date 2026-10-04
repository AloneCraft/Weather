using Weather.Core;

namespace Weather.Presentation;

/// <summary>読み込み済みの地点データ(画面間で共有。詳細画面は地点 ID で引く)。</summary>
public sealed class PlaceData{
    public required string PlaceId{get;init;}
    public required string Name{get;init;}
    public required GeoPoint Point{get;init;}
    public ResolvedLocation? Location{get;set;}
    public ForecastResult? Forecast{get;set;}
    public AlertResult? Alerts{get;set;}
    public string? StationKey{get;set;}
    public bool IsCurrentLocation{get;init;}

    public TimeZoneInfo Zone{
        get{
            if(this.Location is null){
                return TimeZoneInfo.Utc;
            }
            return TimeText.Zone(this.Location.TimeZoneId);
        }
    }
}

public sealed class WeatherSession{
    private readonly Dictionary<string,PlaceData> places=new(StringComparer.Ordinal);

    public event EventHandler<string>? PlaceUpdated;

    public PlaceData? Find(string? placeId){
        if(placeId is null){
            return null;
        }
        this.places.TryGetValue(placeId,out var data);
        return data;
    }

    public void Put(PlaceData data){
        ArgumentNullException.ThrowIfNull(data);
        this.places[data.PlaceId]=data;
        this.PlaceUpdated?.Invoke(this,data.PlaceId);
    }

    public void Remove(string placeId){
        this.places.Remove(placeId);
    }

    public void NotifyUpdated(string placeId){
        this.PlaceUpdated?.Invoke(this,placeId);
    }
}
