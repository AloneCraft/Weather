namespace Weather.Core;

/// <summary>Gfs: NOAA の全球数値予報モデル(地図の格子レイヤー。日本周辺では使わない。方針 5)。</summary>
public enum ProviderId{Jma,Nws,MetNorway,Gfs}

[Flags]
public enum DataProcessing{
    None=0,
    UnitConverted=1,   //単位換算(NWS の °F→°C、km/h→m/s)
    Aggregated=2,      //アプリ側の集計(MET の日別まとめ等)。気象庁データでは禁止
    Interpolated=4,    //格子の補間・色分け(GFS の地図表示)。気象庁データでは地点の予報(Forecast)・矢印・タイルに使わない。例外は地図の海上の風の流れ(海上分布予報を補間。2026-10-04 に利用者が決定)
}

public sealed record LicenseInfo(string Name,Uri? Url);

/// <summary>データの出典。すべてのデータ項目が参照を持つ(方針 7)。</summary>
public sealed record SourceAttribution{
    public required ProviderId Provider{get;init;}
    public required string AgencyName{get;init;}
    public required string ProductName{get;init;}
    public string? PublishingOffice{get;init;}
    public DateTimeOffset? IssuedAt{get;init;}
    public required DateTimeOffset RetrievedAt{get;init;}
    public required LicenseInfo License{get;init;}
    public Uri? SourceUrl{get;init;}
    public DataProcessing Processing{get;init;}
    public bool IsStale{get;init;}
}
