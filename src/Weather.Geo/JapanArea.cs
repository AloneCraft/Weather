using Weather.Core;

namespace Weather.Geo;

/// <summary>
/// 日本周辺域(CacheAndRouting.md)。地点の解決(LocationResolver)と同じ規則: 気象庁の区域の内側、または日本周辺域マスクの中。
/// 地図では、この範囲に GFS を描かない(方針 5)。
/// </summary>
public sealed class JapanArea(GeoDatabase database):IJapanArea{
    //気象庁の区域(離島を含む)を囲む範囲。この外ではポリゴンを調べない
    private const double MinLat=20;
    private const double MaxLat=46;
    private const double MinLon=122;
    private const double MaxLon=154;

    public bool Contains(double latitude,double longitude){
        if(database.JapanMask.Contains(latitude,longitude)){
            return true;
        }
        if(latitude<MinLat||latitude>MaxLat||longitude<MinLon||longitude>MaxLon){
            return false;
        }
        return database.FindJmaAreaContaining(latitude,longitude) is not null;
    }
}
