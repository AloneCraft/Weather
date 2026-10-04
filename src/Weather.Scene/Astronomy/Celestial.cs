using Weather.Core;

namespace Weather.Scene.Astronomy;

/// <summary>太陽位置(NOAA の一般的な太陽位置計算式。誤差はおおむね 0.5° 以内)。</summary>
public static class SolarPosition{
    public static CelestialPosition Compute(double latitude,double longitude,DateTimeOffset time){
        var utc=time.UtcDateTime;
        var daysInYear=365d;
        if(DateTime.IsLeapYear(utc.Year)){
            daysInYear=366;
        }
        var hour=utc.Hour+utc.Minute/60d+utc.Second/3600d;
        var gamma=2*Math.PI/daysInYear*(utc.DayOfYear-1+(hour-12)/24);
        var eqTime=229.18*(0.000075+0.001868*Math.Cos(gamma)-0.032077*Math.Sin(gamma)-0.014615*Math.Cos(2*gamma)-0.040849*Math.Sin(2*gamma));
        var declination=0.006918-0.399912*Math.Cos(gamma)+0.070257*Math.Sin(gamma)-0.006758*Math.Cos(2*gamma)+0.000907*Math.Sin(2*gamma)-0.002697*Math.Cos(3*gamma)+0.00148*Math.Sin(3*gamma);
        var trueSolarTime=hour*60+eqTime+4*longitude;
        var hourAngle=GeoMath.DegreesToRadians(trueSolarTime/4-180);
        var phi=GeoMath.DegreesToRadians(latitude);
        return Horizontal(phi,declination,hourAngle);
    }

    internal static CelestialPosition Horizontal(double phi,double declination,double hourAngle){
        var sinAltitude=Math.Sin(phi)*Math.Sin(declination)+Math.Cos(phi)*Math.Cos(declination)*Math.Cos(hourAngle);
        var altitude=Math.Asin(Math.Clamp(sinAltitude,-1,1));
        var azimuth=Math.Atan2(Math.Sin(hourAngle),Math.Cos(hourAngle)*Math.Sin(phi)-Math.Tan(declination)*Math.Cos(phi));
        return new CelestialPosition(GeoMath.RadiansToDegrees(altitude),GeoMath.NormalizeDegrees(GeoMath.RadiansToDegrees(azimuth)+180));
    }

    public static DaylightPhase Phase(double sunAltitudeDeg){
        if(sunAltitudeDeg>-0.833){
            return DaylightPhase.Day;
        }
        if(sunAltitudeDeg>-6){
            return DaylightPhase.CivilTwilight;
        }
        if(sunAltitudeDeg>-12){
            return DaylightPhase.NauticalTwilight;
        }
        if(sunAltitudeDeg>-18){
            return DaylightPhase.AstronomicalTwilight;
        }
        return DaylightPhase.Night;
    }
}

/// <summary>月の位置・位相(低精度の天文計算式。誤差はおおむね 1° 以内)。</summary>
public static class MoonCalculator{
    private const double Rad=Math.PI/180;
    private const double Obliquity=Rad*23.4397;
    private const double SunDistanceKm=149598000;

    public static MoonState Compute(double latitude,double longitude,DateTimeOffset time){
        var d=DaysSinceJ2000(time);
        var (moonRa,moonDec,moonDistance)=MoonCoordinates(d);
        var (sunRa,sunDec)=SunCoordinates(d);

        var lw=Rad*-longitude;
        var phi=Rad*latitude;
        var siderealTime=Rad*(280.16+360.9856235*d)-lw;
        var hourAngle=siderealTime-moonRa;
        var position=SolarPosition.Horizontal(phi,moonDec,hourAngle);

        var elongation=Math.Acos(Math.Clamp(Math.Sin(sunDec)*Math.Sin(moonDec)+Math.Cos(sunDec)*Math.Cos(moonDec)*Math.Cos(sunRa-moonRa),-1,1));
        var inclination=Math.Atan2(SunDistanceKm*Math.Sin(elongation),moonDistance-SunDistanceKm*Math.Cos(elongation));
        var angle=Math.Atan2(Math.Cos(sunDec)*Math.Sin(sunRa-moonRa),Math.Sin(sunDec)*Math.Cos(moonDec)-Math.Cos(sunDec)*Math.Sin(moonDec)*Math.Cos(sunRa-moonRa));
        var illumination=(1+Math.Cos(inclination))/2;
        var sign=1d;
        if(angle<0){
            sign=-1;
        }
        var phase=0.5+0.5*inclination*sign/Math.PI;
        phase=phase-Math.Floor(phase);
        return new MoonState(position,phase,Math.Clamp(illumination,0,1));
    }

    private static double DaysSinceJ2000(DateTimeOffset time){
        var julian=time.ToUnixTimeMilliseconds()/86400000d+2440587.5;
        return julian-2451545;
    }

    private static (double Ra,double Dec,double DistanceKm) MoonCoordinates(double d){
        var l0=Rad*(218.316+13.176396*d);
        var m=Rad*(134.963+13.064993*d);
        var f=Rad*(93.272+13.229350*d);
        var longitude=l0+Rad*6.289*Math.Sin(m);
        var latitude=Rad*5.128*Math.Sin(f);
        var distance=385001-20905*Math.Cos(m);
        return (RightAscension(longitude,latitude),Declination(longitude,latitude),distance);
    }

    private static (double Ra,double Dec) SunCoordinates(double d){
        var m=Rad*(357.5291+0.98560028*d);
        var c=Rad*(1.9148*Math.Sin(m)+0.02*Math.Sin(2*m)+0.0003*Math.Sin(3*m));
        var perihelion=Rad*102.9372;
        var longitude=m+c+perihelion+Math.PI;
        return (RightAscension(longitude,0),Declination(longitude,0));
    }

    private static double RightAscension(double l,double b){
        return Math.Atan2(Math.Sin(l)*Math.Cos(Obliquity)-Math.Tan(b)*Math.Sin(Obliquity),Math.Cos(l));
    }

    private static double Declination(double l,double b){
        return Math.Asin(Math.Sin(b)*Math.Cos(Obliquity)+Math.Cos(b)*Math.Sin(Obliquity)*Math.Sin(l));
    }
}
