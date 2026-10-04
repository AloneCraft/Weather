namespace Weather.Core;

public enum JmaAreaMatchKind{Inside,Nearest,OutOfCoverage}

/// <summary>日本周辺域の地点に対する気象庁区域(class20)の判定結果。</summary>
public sealed record JmaAreaMatch(JmaAreaMatchKind Kind,string? Class20Code,double DistanceKm){
    public bool IsCovered=>this.Kind!=JmaAreaMatchKind.OutOfCoverage&&this.Class20Code is not null;
}

public sealed record ResolvedLocation{
    public required GeoPoint Point{get;init;}
    public required string CountryCode{get;init;}
    public required string DisplayName{get;init;}
    public required string TimeZoneId{get;init;}
    public string? AdminName{get;init;}
    public JmaAreaMatch? JmaArea{get;init;}

    public bool IsJapanArea=>this.JmaArea is not null;
}

public interface ILocationResolver{
    ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken);
}
