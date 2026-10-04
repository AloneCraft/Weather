# シーン状態と変換ロジック

予報データを、描画に必要な「シーン状態」(空・太陽高度・雲量・降水種別と強度・風・雷・霧)に変換する。変換は `Weather.Scene` の純粋関数で行い、SkiaSharp にも MAUI にも依存しない。

## 方針

- **変換器は純粋関数**: `(Forecast, 時刻) → SceneState`。乱数や現在時刻を使わない。滑らかな遷移やゆらぎは描画側(Rendering)の責務とする。
- **数値データがあれば数値を優先し、なければ天気の分類から決める**: MET・NWS は雲量・降水量・雷確率などの数値を持つ。気象庁はほぼ分類(天気コード・天気文)のみ。
- **降水の有無は Condition か降水量だけで決める**: 降水確率から降水を作り出さない。気象庁の「晴れ・降水確率 30%」で雨を描かないため。
- **日本域**: シーンは気象庁の発表を可視化したものであり、発表と矛盾する演出をしない。対象外の地点(`JapanOutOfCoverage`)は天気を描かず、空(太陽・月・星)のみの中立なシーンにする。

## SceneState

```csharp
namespace Weather.Scene;

public enum DaylightPhase{Night,AstronomicalTwilight,NauticalTwilight,CivilTwilight,Day}
public enum SceneCloudType{Cumulus,Stratus,Cumulonimbus}
public enum ScenePrecipitationType{None,Drizzle,Rain,FreezingRain,RainAndSnow,Snow,IcePellets,Hail}
public enum HazeKind{None,Haze,Smoke,Dust}

public readonly record struct CelestialPosition(double AltitudeDeg,double AzimuthDeg);
public readonly record struct MoonState(CelestialPosition Position,double Phase,double Illumination);   //Phase 0=新月 0.5=満月
public readonly record struct CloudState(double Cover,double Darkness,SceneCloudType Type);            //0..1
public readonly record struct PrecipitationState(ScenePrecipitationType Type,double Intensity,double Intermittency);  //Intermittency 0=連続 1=ほぼ降らない
public readonly record struct WindState(double SpeedMs,double DirectionDeg,double Gustiness);
public readonly record struct HazeState(HazeKind Kind,double Density);

public sealed record SceneState{
    public required DateTimeOffset Time{get;init;}
    public required CelestialPosition Sun{get;init;}
    public required MoonState Moon{get;init;}
    public required DaylightPhase Daylight{get;init;}
    public required CloudState Clouds{get;init;}
    public required PrecipitationState Precipitation{get;init;}
    public required WindState Wind{get;init;}
    public required double ThunderActivity{get;init;}    //0..1
    public required double FogDensity{get;init;}         //0..1
    public required HazeState Haze{get;init;}
    public double? TemperatureC{get;init;}               //将来の演出(霜・陽炎)用
    public required SceneOrigin Origin{get;init;}        //由来(Provider、使った区間、TimeSeries/Daily/なし)
}
```

0..1 の値は生成時に範囲を検証する(範囲外は例外。変換器のバグとして扱う)。

## 変換手順

```csharp
public sealed class SceneConverter(SceneTuning tuning){
    public SceneState Convert(Forecast forecast,DateTimeOffset time){ ... }
}
```

`SceneTuning` はしきい値・係数をまとめたもの。SceneLab で調整して既定値に反映する。

### 1. 区間の選択

時刻 `t` を含む `ForecastPoint` を `TimeSeries` から選ぶ。なければ `DayPart`、それもなければ現地日付の `DailyForecast` を使う。どれもなければ天気なし(空のみ)。

気象庁は時系列予報(3 時間ごと、約 2 日分)が主に使われ、それ以降の日は日別に落ちる。

### 2. 天体

- `SolarPosition.Compute(point, t)`: NOAA の太陽位置計算式(高度・方位)。
- `MoonCalculator`: Meeus の簡略式による月の位置・位相・輝面比。
- 外部ライブラリは使わず自前実装する(小さく、テストで検証できる)。CoordinateSharp 等はライセンス(AGPL / 商用の二重)の都合で使わない。

明暗区分(太陽高度):

| 区分 | 高度 |
|---|---|
| Day | > −0.833°(大気差込み) |
| CivilTwilight | −6° 〜 −0.833° |
| NauticalTwilight | −12° 〜 −6° |
| AstronomicalTwilight | −18° 〜 −12° |
| Night | < −18° |

白夜・極夜は計算から自然に扱える。

### 3. 天気分類からの基本値

| 入力 | シーン |
|---|---|
| SkyCover | 雲量: Clear 0.05 / MostlyClear 0.25 / PartlyCloudy 0.5 / MostlyCloudy 0.75 / Overcast 0.95 / Unknown 0.5 |
| 降水強度 | Light 0.3 / Moderate 0.6 / Heavy 0.9 |
| 降水あり | 雲量は最低 0.8(Showery なら 0.6) |
| IsShowery | 断続性(Intermittency)最低 0.3、雲の種類 Cumulus |
| HasThunder | 雷 0.6(Heavy なら 0.9)、雲の種類 Cumulonimbus |
| HasStrongWind | 風速の数値がなければ 12 m/s |
| Obscuration | Fog → 霧 0.8 / Mist → 霧 0.4 / Haze・Smoke・Dust → Haze 0.5 |
| 連続降水・曇天 | 雲の種類 Stratus |

### 4. 複合天気(時々 / 一時 / 後)

| Transition | 扱い |
|---|---|
| Occasionally(時々) | 副天気を断続性 0.5 で重ねる(区間の約半分で現れる) |
| Temporarily(一時) | 副天気を断続性 0.75 で重ねる(区間の約 1/4 で現れる) |
| Later(後) | 区間が 6 時間以上なら、中点より前は主天気、後は副天気。短い区間は主天気のみ |

断続性は「いつ降るか」を描画側が決定的なノイズ(シード固定)で表現するためのパラメーター。変換器は割合だけを決める。

気象庁の時間修飾(「昼頃から」等)は CompositeCondition が保持しないため、日別からのシーンでは中点で切り替える近似になる。主に使う時系列予報(3 時間)ではこの近似の影響は小さい。

### 5. 数値による補正(ある場合は分類より優先)

| 数値 | シーンへの反映 |
|---|---|
| CloudCoverPercent | 雲量 = 値 / 100 |
| PrecipitationMm | 時間雨量(mm/h)= 量 ÷ 区間の時間。雨: < 0.5 → 0.2 / 0.5〜2.5 → 0.35 / 2.5〜7.6 → 0.6 / 7.6〜50 → 0.85 / ≥ 50 → 1.0。雪(水換算): < 0.5 → 0.3 / 0.5〜2 → 0.6 / > 2 → 0.9。分類が降水を示すのに量が 0 の場合は、分類の強度を使い断続性を 0.6 以上にする |
| PrecipitationProbability | 降水がある場合だけ使う。50% 未満なら断続性を 0.6 以上にする。降水を新たに作らない |
| ThunderProbability | 降水があり 30% 以上なら、雷 = 値 / 100 |
| FogPercent | 霧 = 値 / 100 |
| VisibilityM | < 1 km → 霧 0.8 / < 5 km → 霧 0.4(霧の数値がない場合) |
| WindSpeedMs / WindDirectionDeg / WindGustMs | 風に反映。突風度 = (突風 − 平均) ÷ 平均(0..1 に切り詰め) |
| WindSpeedRangeMs(気象庁時系列) | 範囲の中央値 |
| WindText(気象庁日別) | 「強く」→ 10 m/s、「やや強く」→ 7 m/s、それ以外 3 m/s。演出のための近似であり、数値として表示しない |

### 6. 雲の暗さ

雲の暗さ = 0.2 + 0.5 × 降水強度 + 0.3 × 雷(0..1 に切り詰め)。

## 時間軸の操作(スクラブ)

- `SceneTimeline` が区間の境界ごとに SceneState を事前に計算する。
- 連続値は隣接区間の中点どうしを線形補間する。降水種別などの分類値は、境界でクロスフェードする(描画側)。
- 描画側は目標状態の変化を 2〜3 秒かけて滑らかに追従する(臨界減衰)。

## 不変条件(テストで検証)

1. 同じ入力からは同じ出力(決定的)
2. 降水種別 None ⇔ 降水強度 0
3. Condition に降水がなく降水量も 0 または null なら、降水確率にかかわらず降水種別は None
4. 雷 > 0 は、HasThunder または雷確率 30% 以上のときのみ
5. すべての 0..1 値が範囲内、風向は 0 以上 360 未満
6. 太陽高度・方位は NOAA の基準値と一定誤差(高度 ±0.5°)以内(基準値の収集は**要確認**)
7. 区間境界をまたぐ時間変化で、連続値が補間の範囲外に飛ばない
8. 日本の対象外地点では天気(雲・降水・雷・霧)がすべて 0 / None

## SceneLab での利用

SceneLab は SceneState を直接スライダーで操作して描画を確認するほか、記録済みフィクスチャの Forecast を読み込み、変換結果を時間軸で再生できる。`SceneTuning` の調整結果はコードの既定値に反映する。
