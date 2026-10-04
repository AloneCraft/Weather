namespace Weather.Core;

public readonly record struct GeoPoint{
    public GeoPoint(double latitude,double longitude){
        if(double.IsNaN(latitude)||latitude is <-90 or >90){
            throw new ArgumentOutOfRangeException(nameof(latitude),latitude,"緯度は -90〜90 である必要があります。");
        }
        if(double.IsNaN(longitude)||longitude is <-180 or >180){
            throw new ArgumentOutOfRangeException(nameof(longitude),longitude,"経度は -180〜180 である必要があります。");
        }
        this.Latitude=latitude;
        this.Longitude=longitude;
    }

    public double Latitude{get;}
    public double Longitude{get;}

    /// <summary>MET / NWS へのリクエスト用に小数 2 桁へ丸める(CacheAndRouting.md)。</summary>
    public GeoPoint RoundForRequest(){
        return new GeoPoint(Math.Round(this.Latitude,2,MidpointRounding.AwayFromZero),Math.Round(this.Longitude,2,MidpointRounding.AwayFromZero));
    }

    public double DistanceKmTo(GeoPoint other){
        return GeoMath.HaversineKm(this.Latitude,this.Longitude,other.Latitude,other.Longitude);
    }

    public override string ToString(){
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,$"{this.Latitude:0.####},{this.Longitude:0.####}");
    }
}

public static class GeoMath{
    public const double EarthRadiusKm=6371.0088;

    public static double HaversineKm(double lat1,double lon1,double lat2,double lon2){
        var dLat=DegreesToRadians(lat2-lat1);
        var dLon=DegreesToRadians(lon2-lon1);
        var a=Math.Sin(dLat/2)*Math.Sin(dLat/2)+Math.Cos(DegreesToRadians(lat1))*Math.Cos(DegreesToRadians(lat2))*Math.Sin(dLon/2)*Math.Sin(dLon/2);
        return 2*EarthRadiusKm*Math.Asin(Math.Min(1,Math.Sqrt(a)));
    }

    public static double DegreesToRadians(double degrees){
        return degrees*Math.PI/180;
    }

    public static double RadiansToDegrees(double radians){
        return radians*180/Math.PI;
    }

    /// <summary>角度を 0 以上 360 未満に正規化する。</summary>
    public static double NormalizeDegrees(double degrees){
        var d=degrees%360;
        if(d<0){
            d+=360;
        }
        if(d>=360){
            d=0;
        }
        return d;
    }
}
