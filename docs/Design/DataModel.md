# 共通データモデル

すべて `Weather.Core` に置く。Provider(気象庁・NWS・MET Norway)の差異は Adapter で吸収し、ここで定義する型に正規化する。

## 設計の根拠となる観測事実(2026-10-04 取得)

API は予告なく変わり得るため、前提が崩れた場合はこの節と本文を更新する。

| ソース | 観測内容 |
|---|---|
| 気象庁 府県天気予報 `bosai/forecast/data/forecast/{office}.json` | 2 要素配列(短期・週間)。天気文は全角スペース区切りでコードより情報量が多い(例: コード `201` に対し「くもり　昼過ぎ　まで　時々　晴れ」)。風・波は文章。気温は区域ではなく代表地点(東京・大島・八丈島・父島 等)ごと。週間は信頼度 A/B/C、気温の予測範囲(Upper/Lower)、平年値(`tempAverage` / `precipAverage`)を持つ。欠損は空文字 |
| 気象庁 時系列予報 `bosai/jmatile/data/wdist/VPFD/{class10}.json` | 3 時間ごとの天気(単語)、風(方位・階級 `speed`・範囲 `range` 例 `"3 5"`)、代表地点の気温。階級と範囲の意味は**要確認** |
| アメダス `bosai/amedas/data/point/{station}/{yyyyMMdd_HH}.json` | 10 分ごと。各要素は `[値, 品質フラグ]`。風向は 16 方位コード、`sun1h` は時間単位。品質フラグの意味は**要確認** |
| 気象庁 警報 | 旧 `bosai/warning/data/warning/{office}.json` は 2026-05-28 で更新停止。同日「新たな防災気象情報」(レベル 4 危険警報の新設、名称にレベル番号)が運用開始。新しい取得元は `bosai/warning/data/r8/{office}.json`(詳細は WeatherProviders.md) |
| MET Norway Locationforecast 2.0 compact | 時刻は UTC。約 57 時間先まで 1 時間ごと、以降 6 時間ごと。末尾要素は `instant` のみ。`symbol_code` は区間ごとに `_day` / `_night` 付き。compact に霧・雷確率なし |
| NWS | forecast は 12 時間 periods(先頭が 1 時間だけの場合あり)。`?units=si` で気温は °C になるが風速は `"7 km/h"` の文字列。hourly は気温 °F・露点 °C が混在。グリッドデータは ISO 8601 区間+単位コード付きで skyCover・weather(coverage/intensity)・probabilityOfThunder 等を持つ。アラートは CAP 形式(severity / certainty / urgency / messageType) |
| 観測 | NWS は単位コード+`qualityControl` 付き。MET Norway の観測 API(Frost)はキーが必要なため対象外 → 日本・米国以外は観測データなし |

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| WeatherCondition の表現 | 要素分解(空・降水種別・強度・しゅう雨性・雷・強風・視程障害)+ 複合(時々/一時/後) | シーン変換(SceneState.md)の入力として直接使える。アイコンは要素から導出する |
| 単位 | SI 固定の `double` + 単位付きプロパティ名(`TemperatureC`、`WindSpeedMs` 等) | 単純でシリアライズも素直。換算の誤りは Adapter の実データテストで検出する |
| 出典の粒度 | 各データ項目が `SourceAttribution` の参照を持つ(実体は共有) | 気象庁の日別は 1〜3 日目が短期予報、4 日目以降が週間予報で発表時刻が異なるため |
| 時間別と日別 | `ForecastPoint`(時系列の区間)と `DailyForecast`(現地日付単位)を別の型にする | 日別固有の項目(最高/最低、予測範囲、信頼度)で型が曖昧になるのを避ける |

## 型の一覧

| 型 | 種類 | 役割 |
|---|---|---|
| `SourceAttribution` / `LicenseInfo` | sealed record | 出典(機関名・プロダクト名・発表時刻・取得時刻・ライセンス・加工の有無) |
| `WeatherCondition` | readonly record struct | 要素分解した天気 |
| `CompositeCondition` | readonly record struct | 主天気 +(時々/一時/後)+ 副天気 |
| `ForecastPoint` | sealed record | 時系列の 1 区間(1h / 3h / 6h) |
| `DailyForecast` / `DayPart` | sealed record | 日別予報と日内区分(気象庁の 6 時間降水確率、NWS の昼/夜) |
| `Forecast` | sealed class | 地点の予報全体。コンストラクタで不変条件を検証 |
| `DataIssue` | sealed record | 取得できなかった補助プロダクトの記録(部分結果の明示) |
| `Observation` / `Measurement` / `ObservationStation` / `ObservationSeries` | record / class | 観測値(値ごとに品質を持つ) |
| `Alert` / `AlertSet` | sealed record / class | 警報・注意報 |
| `GeoPoint` / `ResolvedLocation` | 値型 / record | 座標と解決済み地点(国コード、IANA タイムゾーン、気象庁区域の判定結果)。定義は CacheAndRouting.md |
| `GridGeometry` / `GridField` / `WindField` | 値型 / class / record | 地図の格子(正則な緯度経度格子。行 0 が北端、欠損・日本周辺は NaN、経度方向に一周する格子は循環して補間)。値の種類は `FieldQuantity`(風の U・V・風速、気温 ℃、降水強度 mm/h、雲量 %、海面気圧 hPa) |
| `Legend` / `LegendClass` / `MapLegends` | class / record / static | 地図の凡例(区分 [下限, 上限) と ARGB の色。気象庁の凡例の色をそのまま持つ)。値 → 区分、画素の色 → 区分 |
| `JmaTileLayer` | sealed record | 気象庁の地図タイルの 1 層(プロダクト・要素・初期時刻・有効時刻・ズーム・凡例・出典。タイルの URL を作る) |
| `WindArrowSet` / `PointValueSet` | sealed record | 日本周辺の観測・予報の点(アメダスの風・気温、海上分布予報の風向)。補間しない |
| `MapFrame.JapanWind` | `WindField` | 日本周辺の海上の風(海上分布予報の風向 8 方位と風速の階級から作った 0.5° の格子。陸上は NaN)。加工データ(`Interpolated` · `UnitConverted`)で、地図の粒子だけに使う |
| `MapFrame` / `MapAvailability` | sealed record | 地図の 1 コマ(層・時刻・GFS の格子・気象庁のタイル・点・日本周辺の状態・凡例・出典・取れなかった部分)と各データ源の有効時刻 |

コンテナ(`Forecast` / `ObservationSeries` / `AlertSet`)は class にする。record の値の等価性はリストを参照比較するため、意味を誤解させる。

## 意味の約束

- **時刻**: すべて `DateTimeOffset`。日別は地点の現地日付(`DateOnly`)。表示は `ResolvedLocation.TimeZoneId`(IANA)で変換する。MAUI の iOS / Android では `TimeZoneInfo` が IANA ID を扱える。
- **区間**: `ForecastPoint` は `[Start, End)`。瞬間値(気温・湿度・気圧・風・雲量)は Start 時点の値、区間値(天気・降水量・降水確率)は区間全体の値。MET の 1 時間→6 時間の切り替わりも `End - Start` で表す。
- **原文の保持**: `WeatherText` / `WindText` / `WaveText` には機関の文章をそのまま入れ、`SourceCode` に変換前コードを入れる。`CompositeCondition` はアイコン・シーン選択用の近似であり、気象庁の天気文の時間修飾(「昼過ぎまで」等)は表現しない。日本域の表示は常に原文を優先する(方針 5)。
- **単位**: SI 固定(°C、m/s、mm、hPa、%、度)。表示単位への換算は Presentation で行う。NWS の °F / km/h 文字列は Adapter で換算し `Processing` に `UnitConverted` を立てる。
- **風向**: 「吹いてくる方向」の度数。気象庁・NWS の 16 方位は度数へ可逆に変換する。静穏は風向 `null`・風速 0。
- **名前の注意**: 日本の「みぞれ」は `RainAndSnow`、米国の "sleet"(凍雨)は `IcePellets`。`Sleet` という名前は誤対応を招くため使わない。

## 定義

### 出典

```csharp
namespace Weather.Core;

public enum ProviderId{Jma,Nws,MetNorway}

[Flags]
public enum DataProcessing{
    None=0,
    UnitConverted=1,   //単位換算(NWS の °F→°C、km/h→m/s)
    Aggregated=2,      //アプリ側の集計(MET の日別まとめ等)。気象庁データでは禁止
}

public sealed record LicenseInfo(string Name,Uri? Url);

public sealed record SourceAttribution{
    public required ProviderId Provider{get;init;}
    public required string AgencyName{get;init;}         //"気象庁" "National Weather Service" "MET Norway"
    public required string ProductName{get;init;}        //"府県天気予報" "時系列予報" "Locationforecast 2.0"
    public string? PublishingOffice{get;init;}           //気象庁 publishingOffice / NWS 予報官署
    public DateTimeOffset? IssuedAt{get;init;}           //気象庁 reportDatetime / NWS updateTime / MET updated_at
    public required DateTimeOffset RetrievedAt{get;init;}
    public required LicenseInfo License{get;init;}
    public Uri? SourceUrl{get;init;}
    public DataProcessing Processing{get;init;}
    public bool IsStale{get;init;}                       //通信失敗により古いキャッシュを表示している(CacheAndRouting.md)
}
```

各 Provider の具体的な AgencyName / ProductName / License の値は WeatherProviders.md で定義する。

### 天気

```csharp
public enum SkyCover{Unknown,Clear,MostlyClear,PartlyCloudy,MostlyCloudy,Overcast}
public enum PrecipitationType{None,Drizzle,Rain,FreezingRain,RainAndSnow,Snow,IcePellets,Hail}
public enum PrecipitationIntensity{None,Light,Moderate,Heavy}
public enum Obscuration{None,Mist,Fog,Haze,Smoke,Dust}

public readonly record struct WeatherCondition(
    SkyCover Sky,
    PrecipitationType Precipitation=PrecipitationType.None,
    PrecipitationIntensity Intensity=PrecipitationIntensity.None,
    bool IsShowery=false,
    bool HasThunder=false,
    bool HasStrongWind=false,
    Obscuration Obscuration=Obscuration.None);

public enum ConditionTransition{Occasionally,Temporarily,Later}   //時々 / 一時 / 後

public readonly record struct CompositeCondition(
    WeatherCondition Primary,
    ConditionTransition? Transition=null,
    WeatherCondition? Secondary=null);
```

昼夜は `WeatherCondition` に含めない(MET の `_day` / `_night` は捨て、昼夜は太陽高度から計算する)。

### 予報

```csharp
public sealed record ForecastPoint{
    public ForecastPoint(DateTimeOffset start,DateTimeOffset end,SourceAttribution source){
        if(end<=start){
            throw new ArgumentException("end は start より後である必要があります。",nameof(end));
        }
        this.Start=start;
        this.End=end;
        this.Source=source;
    }

    public DateTimeOffset Start{get;}
    public DateTimeOffset End{get;}
    public SourceAttribution Source{get;}

    public CompositeCondition? Condition{get;init;}
    public string? SourceCode{get;init;}       //変換前コード(MET "rainshowers_day" 等)
    public string? WeatherText{get;init;}      //機関の天気文そのまま
    public double? TemperatureC{get;init;}
    public int? PrecipitationProbability{
        get;
        init{
            Guard.Percent(value);
            field=value;
        }
    }
    public double? PrecipitationMm{get;init;}
    public double? WindSpeedMs{get;init;}
    public ValueRange? WindSpeedRangeMs{get;init;}   //気象庁時系列の風速階級
    public double? WindDirectionDeg{
        get;
        init{
            Guard.Direction(value);
            field=value;
        }
    }
    // 他: CloudCoverPercent, HumidityPercent, WindGustMs, PressureHpa, ThunderProbability, VisibilityM
}
```

- 2 値にまたがる不変条件(`Start < End`)はコンストラクタで検証する。`required init` では代入順に依存して検証できないため。get 専用なので `with` でも書き換えられない。
- 範囲検証は C# 14 の `field` キーワードを使った init で行い、`with` 式でも検証が通る。

`DailyForecast` の項目:

| 項目 | 型 | 内容 |
|---|---|---|
| Date | DateOnly | 地点の現地日付 |
| Condition | CompositeCondition? | 代表天気 |
| SourceCode | string? | 気象庁の天気コード等 |
| WeatherText | string? | 気象庁の天気文、NWS shortForecast |
| WindText / WaveText | string? | 気象庁短期予報の文章 |
| TempMaxC / TempMinC | double? | 最高 / 最低気温 |
| TempMaxRangeC / TempMinRangeC | ValueRange? | 気象庁週間の予測範囲 |
| PrecipitationProbability | int? | 日ごとの降水確率(気象庁週間・MET の集計) |
| PrecipitationMm | double? | 日ごとの降水量(MET の集計) |
| TemperaturePointName | string? | 気温の代表地点名(「東京」)。気象庁の数値がどこの気温かを表示するため |
| Parts | IReadOnlyList<DayPart> | 日内区分 |
| Reliability | ForecastReliability? | 気象庁週間の信頼度 A/B/C |
| Source | SourceAttribution | 出典(天気・天気文の出典) |
| TemperatureSource | SourceAttribution? | 気温を別プロダクトから取った場合の出典(気象庁で明後日の天気は短期予報、気温は週間予報となる場合など)。null なら Source と同じ |

`DayPart` は `Start` / `End` / `Label`(NWS の「Tonight」等)/ `Condition` / `SourceCode` / `WeatherText` / `DetailText` / `TemperatureC` / `PrecipitationProbability` を持ち、気象庁の 6 時間降水確率と NWS の昼/夜 periods を同じ型で表す。

```csharp
public sealed class Forecast{
    public Forecast(ResolvedLocation location,IReadOnlyList<ForecastPoint> timeSeries,IReadOnlyList<DailyForecast> daily,string? areaName=null,IReadOnlyList<DataIssue>? issues=null){
        ForecastRules.EnsureAscendingAndNonOverlapping(timeSeries);
        ForecastRules.EnsureStrictlyAscending(daily);
        ForecastRules.EnsureJapanUsesJmaOnly(location,timeSeries,daily);
        this.Location=location;
        this.TimeSeries=[..timeSeries];
        this.Daily=[..daily];
        this.AreaName=areaName;
        this.Issues=[..issues??[]];
    }

    public ResolvedLocation Location{get;}
    public string? AreaName{get;}    //予報区域名("東京地方")。利用者が選んだ地点名とは別
    public IReadOnlyList<ForecastPoint> TimeSeries{get;}
    public IReadOnlyList<DailyForecast> Daily{get;}
    public IReadOnlyList<DataIssue> Issues{get;}    //取得できなかった補助プロダクト
}

public sealed record DataIssue(ProviderId Provider,string ProductName,ProviderFailure Failure);

internal static class ForecastRules{
    public static void EnsureJapanUsesJmaOnly(ResolvedLocation location,IEnumerable<ForecastPoint> timeSeries,IEnumerable<DailyForecast> daily){
        if(location.CountryCode!="JP"){
            return;
        }
        var sources=timeSeries.Select(static p=>p.Source)
            .Concat(daily.Select(static d=>d.Source))
            .Concat(daily.Select(static d=>d.TemperatureSource).OfType<SourceAttribution>());
        foreach(var source in sources){
            if(source.Provider!=ProviderId.Jma){
                throw new InvalidOperationException($"日本域に気象庁以外のデータが含まれています: {source.Provider}");
            }
            if(source.Processing.HasFlag(DataProcessing.Aggregated)){
                throw new InvalidOperationException("気象庁データにアプリ側の集計を適用してはいけません。");
            }
        }
    }
}
```

### 観測

```csharp
public enum MeasurementQuality{Normal,Suspect}    //Suspect = 気象庁の準正常値 / NWS の QC 要注意値
public readonly record struct Measurement(double Value,MeasurementQuality Quality=MeasurementQuality.Normal);
```

`Observation` は `ObservedAt` と、次の `Measurement?` 項目を持つ: `TemperatureC` / `HumidityPercent` / `Precipitation10mMm` / `Precipitation1hMm` / `Precipitation24hMm` / `WindSpeedMs` / `WindDirectionDeg` / `WindGustMs` / `PressureHpa` / `SeaLevelPressureHpa` / `Sunshine1hHours` / `SnowDepthCm` / `VisibilityM`。加えて `Condition` と `WeatherText`。

- 準正常値は捨てずに `Suspect` として残し、気象庁サイトと同様に印付きで表示する。
- 欠測は `null`。

`ObservationStation` は `Id` / `Provider` / `Name` / `Location`(GeoPoint)/ `ElevationM` を持ち、`Key`(例 `Jma:44132`)で識別する。`ObservationSeries` は観測所・観測値の一覧(時刻昇順)・出典に加え、機関が計算した日ごとの値 `DailySummaries`(気象庁の日最高・最低。提供しない Provider では空)を持つ。

### 警報

```csharp
public enum AlertTier{Information,Advisory,Watch,Warning,Danger,Emergency}
public enum AlertStatus{Active,Updated,Downgraded,Cancelled}
```

`Alert` の項目: `Id` / `EventName`(機関の名称そのまま。例「レベル4大雨危険警報」「Flash Flood Warning」)/ `EventCode` / `Tier` / `WarningLevel`(int?。気象庁の警戒レベル相当 2〜5)/ `Status` / `Onset` / `Expires` / `AreaName` / `Headline` / `Description` / `Instruction` / `SourceSeverity`(CAP severity の原文)/ `Source`。

- `Danger` は気象庁の「危険警報」(レベル 4)用。特別警報と NWS の "... Emergency" は `Emergency`。
- `AlertSet` は警報の一覧・出典・見出し文(気象庁 headlineText 相当)を持つ。
- 「警報なし」(空の AlertSet)と「その地域は警報データ非対応」(IAlertProvider が存在しない)は区別する。詳細は WeatherProviders.md。

## Core に置く抽象

Infrastructure・Geo・Providers が Core にのみ依存して実装できるよう、利用側(Presentation)が使う抽象も Core に置く。

| 抽象 | 実装 |
|---|---|
| `IWeatherService`(解決・振り分け・取得の窓口) | Providers の `WeatherService`(Functions 移行時は遠隔実装に差し替え) |
| `ILocationResolver` / `IPlaceSearch` | Geo |
| `IHttpCacheStore` | Infrastructure(ファイル)/ Providers(メモリ) |
| `IFavoritesStore` / `IObservationHistoryStore` / `IObservationHistoryService` | Infrastructure(SQLite) |
| `IMapDataService`(地図の時刻の一覧・コマ・気象庁のタイル) | Providers の `MapDataService` |
| `IJapanArea`(日本周辺域の判定。地点の解決と同じ規則) | Geo の `JapanArea` |
| `IAppActivity`(アプリの使用中か。MET の取得制限) | App の `AppLifecycle`(サーバー・テストは常に使用中) |

## 不変条件

1. すべてのデータ項目が `Source` を持つ(コンストラクタ引数であり、渡し忘れはコンパイルエラー)→ 方針 7
2. `ForecastPoint`: `Start < End`。確率・湿度は 0〜100、風向は 0 以上 360 未満
3. `TimeSeries` は Start 昇順かつ区間が重ならない。`Daily` は日付昇順かつ重複なし
4. 国コード JP の `Forecast` は気象庁データのみで構成され、`Aggregated`・`Interpolated` を含まない → 方針 5 を実行時例外で強制
5. 降水種別 `None` ⇔ 強度 `None`。`Transition` が null ⇔ `Secondary` が null(変換表テストで網羅検証)
6. モデルは不正値を受け付けない。API の異常値は Adapter が `null` にして診断ログへ記録する(WeatherProviders.md)
7. `GridField`: 値の数 = 格子の点数、有効時刻 ≧ 初期時刻。GFS の格子は日本周辺の格子点が NaN(方針 5。Providers で設定し、描画・吹き出しでも重ねて防ぐ)

## 変換例(全体の変換表は WeatherProviders.md の付録)

| 入力 | 結果 |
|---|---|
| 気象庁 `201`(曇時々晴) | `Primary(Overcast)` / `Occasionally` / `Secondary(Clear)`。表示は天気文の原文 |
| MET `lightrainshowers_day` | `(PartlyCloudy, Rain, Light, IsShowery)` |
| MET `heavysnowandthunder` | `(Overcast, Snow, Heavy, HasThunder)` |
| NWS グリッド `{coverage:"chance", weather:"rain_showers", intensity:"light"}` | `(Unknown, Rain, Light, IsShowery)`。空は skyCover、確率は降水確率から別に取る |

## 補足

- 観測は日本(アメダス)と米国(NWS)のみ取得できる。他地域は「観測データなし」と明示する(History.md)。
- 平年値(気象庁週間 `tempAverage` / `precipAverage`)は現時点でモデルに含めない。平年比の表示が必要になった時点で追加する。
- 時系列予報の風速階級とアメダスの品質フラグの意味は**要確認**(WeatherProviders.md)。
