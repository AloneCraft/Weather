namespace Weather.Core;

public enum ProviderId{Jma,Nws,MetNorway}

[Flags]
public enum DataProcessing{
    None=0,
    UnitConverted=1,   //単位換算(NWS の °F→°C、km/h→m/s)
    Aggregated=2,      //アプリ側の集計(MET の日別まとめ等)。気象庁データでは禁止
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
