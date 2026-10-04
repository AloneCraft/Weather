using Weather.Core;

namespace Weather.Remote.Contracts;

//Functions(サーバー)とアプリ(RemoteWeatherService)の間の電文。Core のモデルとは分け、互換性を電文側で管理する。
//JSON は camelCase、列挙は文字列(RemoteJson)。出典は Sources 表に 1 回だけ載せ、各項目は番号で参照する。

public sealed record GeoPointDto(double Latitude,double Longitude);

public sealed record JmaAreaDto(JmaAreaMatchKind Kind,string? Class20Code,double DistanceKm);

public sealed record LocationDto(GeoPointDto Point,string CountryCode,string DisplayName,string TimeZoneId,string? AdminName,JmaAreaDto? JmaArea);

public sealed record RetentionDto(ProviderId Provider,double Days);

/// <summary>地点の解決結果と、同期で答える必要がある情報(観測の可否・背景取得の可否・観測の保持期間)。</summary>
public sealed record ResolveResponse(LocationDto Location,Availability ObservationAvailability,bool AllowsBackgroundFetch,IReadOnlyList<RetentionDto> Retention);

public sealed record LicenseDto(string Name,string? Url);

public sealed record SourceDto(
    ProviderId Provider,
    string AgencyName,
    string ProductName,
    string? PublishingOffice,
    DateTimeOffset? IssuedAt,
    DateTimeOffset RetrievedAt,
    LicenseDto License,
    string? SourceUrl,
    DataProcessing Processing,
    bool IsStale);

public sealed record ConditionDto(
    SkyCover Sky,
    PrecipitationType Precipitation,
    PrecipitationIntensity Intensity,
    bool IsShowery,
    bool HasThunder,
    bool HasStrongWind,
    Obscuration Obscuration);

public sealed record CompositeConditionDto(ConditionDto Primary,ConditionTransition? Transition,ConditionDto? Secondary);

public sealed record RangeDto(double Lower,double Upper);

public sealed record ForecastPointDto{
    public required DateTimeOffset Start{get;init;}
    public required DateTimeOffset End{get;init;}
    public required int Source{get;init;}
    public CompositeConditionDto? Condition{get;init;}
    public string? SourceCode{get;init;}
    public string? WeatherText{get;init;}
    public double? TemperatureC{get;init;}
    public int? PrecipitationProbability{get;init;}
    public double? PrecipitationMm{get;init;}
    public int? CloudCoverPercent{get;init;}
    public int? HumidityPercent{get;init;}
    public double? WindSpeedMs{get;init;}
    public RangeDto? WindSpeedRangeMs{get;init;}
    public double? WindGustMs{get;init;}
    public double? WindDirectionDeg{get;init;}
    public double? PressureHpa{get;init;}
    public int? ThunderProbability{get;init;}
    public int? FogPercent{get;init;}
    public double? VisibilityM{get;init;}
}

public sealed record DayPartDto{
    public required DateTimeOffset Start{get;init;}
    public required DateTimeOffset End{get;init;}
    public string? Label{get;init;}
    public CompositeConditionDto? Condition{get;init;}
    public string? SourceCode{get;init;}
    public string? WeatherText{get;init;}
    public string? DetailText{get;init;}
    public double? TemperatureC{get;init;}
    public int? PrecipitationProbability{get;init;}
}

public sealed record DailyDto{
    public required DateOnly Date{get;init;}
    public required int Source{get;init;}
    public int? TemperatureSource{get;init;}
    public CompositeConditionDto? Condition{get;init;}
    public string? SourceCode{get;init;}
    public string? WeatherText{get;init;}
    public string? WindText{get;init;}
    public string? WaveText{get;init;}
    public double? TempMaxC{get;init;}
    public double? TempMinC{get;init;}
    public RangeDto? TempMaxRangeC{get;init;}
    public RangeDto? TempMinRangeC{get;init;}
    public string? TemperaturePointName{get;init;}
    public double? PrecipitationMm{get;init;}
    public int? PrecipitationProbability{get;init;}
    public IReadOnlyList<DayPartDto> Parts{get;init;}=[];
    public ForecastReliability? Reliability{get;init;}
}

public sealed record DataIssueDto(ProviderId Provider,string ProductName,ProviderFailure Failure);

public sealed record ForecastDto(
    LocationDto Location,
    string? AreaName,
    IReadOnlyList<SourceDto> Sources,
    IReadOnlyList<ForecastPointDto> TimeSeries,
    IReadOnlyList<DailyDto> Daily,
    IReadOnlyList<DataIssueDto> Issues);

public sealed record ForecastResponse(Availability Availability,ForecastDto? Forecast);

public sealed record AlertDto{
    public required string Id{get;init;}
    public required string EventName{get;init;}
    public required AlertTier Tier{get;init;}
    public required AlertStatus Status{get;init;}
    public required int Source{get;init;}
    public string? EventCode{get;init;}
    public int? WarningLevel{get;init;}
    public DateTimeOffset? Onset{get;init;}
    public DateTimeOffset? Expires{get;init;}
    public string? AreaName{get;init;}
    public string? Headline{get;init;}
    public string? Description{get;init;}
    public string? Instruction{get;init;}
    public string? SourceSeverity{get;init;}
}

public sealed record AlertSetDto(IReadOnlyList<SourceDto> Sources,int Source,string? Headline,IReadOnlyList<AlertDto> Alerts);

public sealed record AlertResponse(Availability Availability,AlertSetDto? Alerts);

public sealed record StationDto(string Id,ProviderId Provider,string Name,GeoPointDto Location,double? ElevationM);

public sealed record StationsResponse(IReadOnlyList<StationDto> Stations);

public sealed record MeasurementDto(double Value,MeasurementQuality Quality);

public sealed record ObservationDto{
    public required DateTimeOffset ObservedAt{get;init;}
    public MeasurementDto? TemperatureC{get;init;}
    public MeasurementDto? HumidityPercent{get;init;}
    public MeasurementDto? Precipitation10mMm{get;init;}
    public MeasurementDto? Precipitation1hMm{get;init;}
    public MeasurementDto? Precipitation24hMm{get;init;}
    public MeasurementDto? WindSpeedMs{get;init;}
    public MeasurementDto? WindDirectionDeg{get;init;}
    public MeasurementDto? WindGustMs{get;init;}
    public MeasurementDto? PressureHpa{get;init;}
    public MeasurementDto? SeaLevelPressureHpa{get;init;}
    public MeasurementDto? Sunshine1hHours{get;init;}
    public MeasurementDto? SnowDepthCm{get;init;}
    public MeasurementDto? VisibilityM{get;init;}
    public CompositeConditionDto? Condition{get;init;}
    public string? WeatherText{get;init;}
}

public sealed record DailySummaryDto(
    string StationKey,
    DateOnly LocalDate,
    double? MaxTempC,
    DateTimeOffset? MaxTempAt,
    double? MinTempC,
    DateTimeOffset? MinTempAt,
    double? PrecipitationMm,
    bool IsDerived);

public sealed record ObservationSeriesDto(StationDto Station,SourceDto Source,IReadOnlyList<ObservationDto> Items,IReadOnlyList<DailySummaryDto> DailySummaries);

/// <summary>Provider の失敗(502)・要求の誤り(400)・不明な経路(404)。クライアントは WeatherProviderException に戻す。</summary>
public sealed record ErrorResponse(ProviderId? Provider,ProviderFailure? Failure,string Message);
