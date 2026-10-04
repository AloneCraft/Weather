# Provider インターフェースと各 Adapter

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| インターフェースの形 | 役割ごとに分割(IForecastProvider / IAlertProvider / IObservationProvider) | MET は予報のみ。非対応を「実装がない」ことで型として表せる |
| MET のエンドポイント | Locationforecast 2.0 **complete** | 霧・雷確率・降水確率・6 時間最高/最低気温・突風が得られ、シーンと日別の精度が上がる(サイズは compact の約 2.5 倍) |
| 部分的な失敗 | 主プロダクトが取れれば部分結果を返し、欠けた補助プロダクトを `Forecast.Issues` に記録 | 補助プロダクトの一時障害で予報全体が見られなくなるのを避ける |
| User-Agent の連絡先 | GitHub リポジトリ URL(`github.com/AloneCraft/Weather`) | MET の規約例にもある形式。個人の連絡先を出さない。リポジトリが公開されていることが前提 |
| 地図の格子(2026-10-04) | 日本周辺以外は NOAA GFS 0.5°(AWS Open Data)、日本周辺は気象庁の地図タイル(jmatile)とアメダス | 地図トップに面のデータが必要。GFS はパブリックドメインでキー不要、Range 要求で 1 要素だけ取れる。日本周辺は方針 5 により気象庁だけ |

## 利用規約・法令(2026-10-04 確認)

| 対象 | 内容 |
|---|---|
| 気象庁 | ホームページのコンテンツは「公共データ利用規約(第 1.0 版)」に準拠。出典記載例「出典:気象庁ホームページ(当該ページの URL)」。編集・加工した場合はその旨を別途記載し、加工した情報を国が作成したかのような態様で公表しない。気象業務法第 17 条(予報業務の許可)・第 23 条(警報の制限)への注意喚起あり |
| MET Norway | User-Agent による識別が必須(ブラウザ JS は Origin ヘッダーで可)。座標は小数 4 桁まで(5 桁以上は 403)。Expires まで再取得しない、If-Modified-Since を使う(値は直前の Last-Modified と同一)。モバイルアプリは使用中でないときにデータを取得してはいけない。全インストール合計で 20 req/s を超える場合は特別な合意が必要。CC BY 4.0(クレジット・ライセンスへのリンク・改変の有無の表示)および NLOD 2.0 |
| NWS | User-Agent が必須(連絡先を含めることを推奨)。将来 API キーに置き換える予定と明記。レート制限値は非公開(超過時はエラー、通常数秒で解除) |
| NOAA GFS(AWS Open Data) | 米国政府の著作物でパブリックドメイン。AWS Open Data の登録(noaa-gfs-bdp-pds)でキー不要・無料で公開。出典の表示は義務ではないが、方針 7 により「NOAA GFS」と初期時刻を表示する。数値予報モデルの計算結果をそのまま色分けして表示し、アプリ独自の予報は作らない(日本周辺では表示しない。気象業務法の扱いは公開前に再確認、**要確認**) |
| 気象庁の地図タイル | 気象庁ホームページのコンテンツとして公共データ利用規約の範囲で扱う(出典「出典:気象庁ホームページ」)。jmatile は画面用のデータで、URL・ズーム・凡例は予告なく変わり得る。アプリからの機械的な取得の条件(アクセス頻度の制限など)は**要確認**。HTTP キャッシュで同じタイルを取り直さない |

bosai 系 JSON は公開仕様ではなく、気象庁サイトの画面用データである。URL・構造・コード表は予告なく変わり得る。

## ガードレール

1. **気象業務法第 23 条**: 日本域で警報・注意報に相当する情報を独自に作らない。気象庁の警報は名称・発表官署・発表時刻とともにそのまま表示する。
2. **気象庁の出典表示**: 「出典:気象庁ホームページ」と URL を表示する。加工した場合はその旨を別に表示する。
3. **MET の取得制限**: アプリが使用中でないときは取得しない(ウィジェット・バックグラウンド更新での MET 取得は禁止)。座標は小数 4 桁以下。二重に防ぐ: (a) 背景更新は `IWeatherService.AllowsBackgroundFetch` が true の地点(気象庁・NWS)だけを取得する。(b) `MetNorwayProvider` は `IAppActivity.IsInUse` が false なら通信せずに `ProviderFailure.NotAllowed` で失敗する(アプリは前面/背景で判定。背景更新だけでプロセスが起動した場合は前面にならない。サーバーでは常に使用中)。
4. **User-Agent**: 全 Provider で `WeatherApp/{version} github.com/{owner}/{repo}` 形式を共通ハンドラで必ず付与する。連絡先は公開リポジトリ `github.com/AloneCraft/Weather`(2026-10-04 公開)。
5. **自動リトライはしない**: 失敗時はキャッシュを表示し、利用者操作で再取得する(CacheAndRouting.md)。MET 規約の再要求の戒めと、症状隠しを避ける方針による。
6. **未知のコードは例外にしない**: Condition を null にして診断ログへ記録し、表示は原文で行う。

## インターフェース

```csharp
namespace Weather.Core;

public interface IWeatherProvider{
    ProviderId Id{get;}
}

public interface IForecastProvider:IWeatherProvider{
    ValueTask<Forecast> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken);
}

public interface IAlertProvider:IWeatherProvider{
    ValueTask<AlertSet> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken);
}

public interface IObservationProvider:IWeatherProvider{
    ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken);
    ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken);
}

public enum ProviderFailure{Network,Timeout,RateLimited,Forbidden,NotFound,ServerError,InvalidResponse}

public sealed class WeatherProviderException(ProviderId provider,ProviderFailure failure,string message,Exception? inner=null):Exception(message,inner){
    public ProviderId Provider{get;}=provider;
    public ProviderFailure Failure{get;}=failure;
}
```

| Provider | 予報 | 警報 | 観測 |
|---|---|---|---|
| 気象庁 | ✓ | ✓ | ✓(アメダス) |
| NWS | ✓ | ✓ | ✓ |
| MET Norway | ✓ | — | — |

- キャンセルは `OperationCanceledException` をそのまま伝播する。
- 主プロダクトの失敗は `WeatherProviderException`。補助プロダクトの失敗は `Forecast.Issues` に記録する。

| Provider | 主プロダクト | 補助プロダクト |
|---|---|---|
| 気象庁 | 府県天気予報 | 時系列予報 |
| NWS | 12 時間予報 | hourly、グリッドデータ |
| MET | Locationforecast(単一) | — |

## Providers の内部構造

```
Weather.Providers/
├─ Http/        UserAgentHandler、HttpCacheHandler(CacheAndRouting.md)、ステータス→ProviderFailure 変換
├─ Conditions/  SkyCoverClassifier(雲量%→SkyCover の共通しきい値)
├─ Jma/         JmaEndpoints, Dto/, JmaJsonContext, JmaAreaCatalog, JmaWeatherCodes, JmaWarningCodes, *Mapper, JmaProvider
├─ Nws/         NwsEndpoints, Dto/, NwsJsonContext, NwsForecastPhraseParser, *Mapper, NwsProvider
└─ MetNorway/   MetEndpoints, Dto/, MetJsonContext, MetSymbolParser, *Mapper, MetProvider
```

- HTTP 取得と変換を分離する。Mapper は「DTO+文脈(地点・取得時刻)→ model」の純粋関数とし、記録した実レスポンスをフィクスチャにテストする。
- URL とコード表は一元管理されたデータとする。記録フィクスチャによる契約テストと、実 API の定期スモークテストで変更を検知する(TestStrategy.md)。
- JSON は `JsonDocument` で読む(気象庁の配列の構造が不規則なため DTO より扱いやすく、リフレクションを使わないので AOT / トリミングでも安全)。保存するデータ(キャッシュのメタデータ等)は source generator を使う。

## 雲量→空模様(共通)

オクタ(8 分割)基準: 0〜12% Clear / 13〜37% MostlyClear / 38〜62% PartlyCloudy / 63〜87% MostlyCloudy / 88% 以上 Overcast。NWS 公式区分との一致は**要確認**。

## 気象庁 Adapter

### 入力と地域解決

- 入力: 地点の class20 コード(Geo が解決した `ResolvedLocation.JmaArea.Class20Code`。CacheAndRouting.md)。
- `area.json` で class20 → class15 → class10 → office をたどる。
- 予報ファイルの office と気温の代表地点は `forecast_area.json`(キー = 予報ファイルの office、値 = class10・アメダス地点・代表 class20)から引く。office コードが特殊な地域もこの表で吸収する。
- 週間予報の区域対応(週間の area が class10 と一致しない地域)は**要確認**(気象庁ページの WEEK_AREAS 定数を調べる)。

### エンドポイント(2026-10-04 確認)

| プロダクト | URL | Cache-Control | 用途 |
|---|---|---|---|
| 府県天気予報(短期+週間) | `https://www.jma.go.jp/bosai/forecast/data/forecast/{office}.json` | max-age=60、Last-Modified、ETag | 日別 |
| 時系列予報 | `https://www.jma.go.jp/bosai/jmatile/data/wdist/VPFD/{class10}.json` | max-age=300 | 時系列(3 時間) |
| 警報・注意報 | `https://www.jma.go.jp/bosai/warning/data/r8/{office}.json` | max-age=60 | 警報 |
| アメダス最新時刻 | `https://www.jma.go.jp/bosai/amedas/data/latest_time.txt` | — | 観測 |
| アメダス地点データ | `https://www.jma.go.jp/bosai/amedas/data/point/{station}/{yyyyMMdd}_{HH}.json` | max-age=60 | 観測(HH は JST の 3 時間ブロック先頭) |
| 地域定義 | `https://www.jma.go.jp/bosai/common/const/area.json` | — | 地域階層 |
| 予報区と気温地点 | `https://www.jma.go.jp/bosai/forecast/const/forecast_area.json` | — | 予報ファイル・代表地点 |
| アメダス地点表 | `https://www.jma.go.jp/bosai/amedas/const/amedastable.json` | — | 観測所(座標は度・分) |

旧警報 `bosai/warning/data/warning/{office}.json` は 2026-05-28 で更新停止しているため使わない。

### 日別予報(気象庁サイトの表示ロジックを再現)

- **天気・天気文・風・波**: 短期予報に含まれる日(2〜3 日分)は短期予報、それ以降は週間予報。
- **気温**:
  - 05 時・11 時発表: 今日の最高 = `temps[0]`、明日の最低 = `temps[2]`、明日の最高 = `temps[3]`
  - 17 時発表: 明日の最低 = `temps[0]`、明日の最高 = `temps[1]`
  - 明後日以降: 週間予報の `tempsMin` / `tempsMax`(予測範囲 Upper / Lower 付き)
  - 気温を週間予報から取った日は `DailyForecast.TemperatureSource` に週間予報の出典を持たせる
- **降水確率**: 今日 = 短期予報の 6 時間ごとの値の先頭 `len % 4` 件、明日 = その次の 4 件(`DayPart`)。明後日以降 = 週間予報の日ごとの値。
- **信頼度**: 週間予報の空文字でない値。
- 発表時刻の区別は `reportDatetime` の時で判定する。

### 時系列予報

- `areaTimeSeries`: 3 時間区間の天気(単語)、風向(16 方位→度)、風速階級の範囲(`range` 例 `"3 5"` → `WindSpeedRangeMs`)。階級と範囲の意味は**要確認**。
- `pointTimeSeries`: 代表地点の気温(時刻ごと)。
- 天気の単語は天気分布予報の区分を想定し、晴れ → Clear / くもり → Overcast / 雨 → Rain / 雨または雪 → RainAndSnow / 雪 → Snow とする。区分の一覧は**要確認**。

### 警報(r8)

- ファイルは配列で、電文種別(`dataTypeCode`: VPWW55 / 56 / 57 / 58 / 59 / 61 を観測)ごとに最新の発表が 1 件ずつ入る。全要素を統合する。種別の意味は**要確認**。
- 地点の class20 に該当する `class20Items[].kinds` を集める。
- 状態: 「発表」「継続」→ Active、「解除」→ Cancelled、「発表警報・注意報はなし」→ 対象外。その他の文字列は**要確認**。
- `headlineText` は AlertSet の見出し、`publishingOffice` と `reportDatetime` は出典に使う。
- `kinds[].properties`(濃霧危険度等)と `additions` は MVP では使わない。
- コード表は気象庁ページの JS から抽出した(付録 C)。公開仕様ではないため、データとして差し替え可能にする。

### 観測(アメダス)

- `amedastable.json` から最寄りの地点を選ぶ(座標は `[度, 分]`)。`elems` のビット並びの意味は**要確認**。
- `latest_time.txt` で最新時刻を得て、3 時間ブロックのファイルを読む。
- 各要素は `[値, 品質フラグ]`。暫定対応: 0 → Normal、1 → Suspect、それ以外 → null(意味は**要確認**)。
- 風向コード: コード × 22.5°(16 = 北 = 0°)、0 = 静穏(風向 null・風速 0)。
- `sun1h` は時間単位、`sun10m` は分単位。

### 天気コード → WeatherCondition

118 件の表(付録 A)を次の規則で生成し、レビューしたうえでデータとして保存する。全コードの網羅はテストで検証する。

| 規則 | 変換 |
|---|---|
| 基本語 | 晴 → Clear / 曇 → Overcast / 雨 → Rain / 雪 → Snow / 雨か雪・雪か雨・みぞれ → RainAndSnow / 霧 → Overcast+Fog / 大雨・大雪 → Heavy / 雷雨・「で雷を伴う」→ Thunder / 暴風・風雪強い・暴風雪 → StrongWind |
| 接続 | 時々 → Occasionally / 一時 → Temporarily / 後 → Later。副天気が降水で「時々」「一時」を伴う場合は Showery |
| 時間の修飾 | 「朝の内X後Y」「X昼頃から/夕方から/夜は/午後はY」→ Later。「X朝夕/朝晩/朝の内/夕方一時Y」→ Temporarily。「X日中時々Y」→ Occasionally |
| 場所の修飾 | 「山沿い」「海上海岸は」→ 主天気のみ(平地の天気を優先) |

「雨か雪」は「雨または雪(どちらか)」の意味で、みぞれ(混在)とは異なるが、シーン表現上は RainAndSnow に寄せる。表示は常に原文。

## NWS Adapter

### 取得の流れ

1. `GET /points/{lat},{lon}`(座標は小数 4 桁まで。max-age=86400 を観測)→ gridId / gridX / gridY、forecast URL、observationStations、timeZone、relativeLocation。
2. **日別**: `GET /gridpoints/{office}/{x},{y}/forecast?units=si`
   - 昼(`isDaytime`)と夜の period を現地日付でまとめる。
   - TempMax = 昼の period、TempMin = **その日の夜の period**(NWS の慣習は「今夜の最低」で、気象庁の「朝の最低」とは意味が違う。UI で区別する)。
   - Parts = 昼・夜。`DetailText` = detailedForecast。
   - 気温は NWS 側で °C 換算済み。風速は `"10 to 15 km/h"` 形式の文字列を解析し `UnitConverted` を立てる。
3. **時系列**: `GET .../forecast/hourly?units=si` を土台に、グリッドデータ `GET /gridpoints/{office}/{x},{y}` の skyCover / probabilityOfThunder / quantitativePrecipitation / visibility / windGust で補う。グリッドの値は ISO 8601 区間(例 `PT2H`)と単位コード付きのため 1 時間ごとに展開し SI へ換算する。
4. **警報**: `GET /alerts/active?point={lat},{lon}`
   - Tier: event 名の末尾 Emergency / Warning / Watch / Advisory、それ以外は Information。
   - Status: messageType Alert → Active、Update → Updated、Cancel → Cancelled。
   - EventCode = `eventCode.NationalWeatherService[0]`、SourceSeverity = severity、Expires = ends(なければ expires)。
5. **観測**: points の observationStations から観測所を選び、`GET /stations/{id}/observations?start=&end=`。単位コードに従い SI へ換算(Pa → hPa、km/h → m/s)。`qualityControl` の暫定対応: X → null、Q → Suspect、それ以外 → Normal(意味は**要確認**)。

`/icons` 系エンドポイントは OpenAPI で deprecated のため、アイコン URL から天気を読み取らない。

### 天気の判定

1. **shortForecast の語句解析**(NWS 自身の判断を使う)
   - `" then "` で分割し、前を主天気、後を副天気(Later)とする。
   - 確率語(Slight Chance / Chance / Likely / Isolated / Scattered / Patchy / Areas Of / Widespread)を除去する。
   - キーワード表で変換する: Sunny / Clear → Clear、Mostly Sunny / Mostly Clear → MostlyClear、Partly Sunny / Partly Cloudy → PartlyCloudy、Mostly Cloudy → MostlyCloudy、Cloudy → Overcast、Rain → Rain、Showers → Rain+Showery、Thunderstorms → Rain+Showery+Thunder、Snow → Snow、Snow Showers → Snow+Showery、Sleet → IcePellets、Freezing Rain → FreezingRain、Drizzle → Drizzle、Fog → Fog、Haze → Haze、Smoke → Smoke、Blowing Dust → Dust、Breezy / Windy → StrongWind。
2. 解析できない場合はグリッドの weather と skyCover から導く(付録 B)。intensity: very_light・light → Light、moderate → Moderate、heavy → Heavy。attributes: heavy_rain → Heavy、gusty_wind・damaging_wind → StrongWind。

## MET Norway Adapter

- `GET https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={lat}&lon={lon}`。座標は小数 4 桁に切り詰める。altitude パラメータの扱いは**要確認**。
- 時刻は UTC。約 57 時間先まで 1 時間ごと、以降 6 時間ごと(2026-10-04 観測)。

### 時系列

- 各要素の区間は「次の要素までの間隔」に合う予報を選ぶ。間隔 1 時間なら `next_1_hours`、6 時間なら `next_6_hours`。これで区間が重ならず隙間なく連続する。
- 末尾の `instant` のみの要素は、直前の間隔を End に使い、天気なしで含める。
- 使う値: instant の気温・湿度・気圧・風・突風・雲量・霧(`fog_area_fraction`)、期間の降水量・降水確率・雷確率(`probability_of_thunder`)。

### シンボル → WeatherCondition

末尾の `_day` / `_night` / `_polartwilight` を除去し、規則で分解する(付録 D)。

- `light` / `heavy` → 強度。`showers` → Showery(空は PartlyCloudy)。`andthunder` → Thunder。
- 本体: rain → Rain、sleet → RainAndSnow(ノルウェー語 sludd は「みぞれ」)、snow → Snow。
- 天気のみ: clearsky → Clear、fair → MostlyClear、partlycloudy → PartlyCloudy、cloudy → Overcast、fog → Overcast+Fog。
- 誤綴りのまま運用されている `lightssleetshowersandthunder` / `lightssnowshowersandthunder` を受け付ける。正しい綴りも受け付ける。

### 日別(アプリ側集計。`Processing = Aggregated`)

- 現地日付で集計する。最高 / 最低気温は `next_6_hours` の `air_temperature_max` / `air_temperature_min`。
- 代表天気は日中の 6 時間ブロックで最も顕著なもの(雷 > 強い降水 > 降水 > 霧 > 曇り)。
- 降水確率は 6 時間ごとの最大値(厳密な合成ではない)。
- UI に「MET Norway のデータをもとにアプリで集計」と表示する(CC BY 4.0 の改変表示)。

## 地図のデータ(MapDataService)

地図の 1 コマ(`MapFrame`)を層と時刻ごとに組み立てる。日本周辺(`IJapanArea`。地点の解決と同じ範囲)は気象庁、それ以外は GFS。

| 層 | 日本周辺(気象庁) | 日本周辺以外(GFS) |
|---|---|---|
| 風 | 陸上: 現在時刻(アメダスの最新観測から 30 分以内)はアメダスの風向・風速の矢印。陸上の風の予報はないため、それ以降は空白。海上: 海上分布予報(前後 3 時間。現在時刻も最も近い時刻)の風向の矢印と、風向・風速の階級から作った流れ(粒子。下記) | 地上 10 m の U・V(粒子)と風速(色) |
| 雨・雪 | 1 時間先まで: 雨雲の動き(前後 3 分)。15 時間先まで: 今後の雨(前後 30 分)。その後: 天気分布予報の 3 時間降水量(前後 90 分) | 地上の降水強度(瞬間値、mm/h) |
| 気温 | 天気分布予報の気温(前後 90 分)。現在時刻はアメダスの気温も重ねる | 地上 2 m の気温 |
| 雲・気圧 | 天気分布予報の天気(前後 90 分) | 全雲量と海面気圧(等圧線) |

- 気象庁の予報期間を過ぎた時刻は `JapanCoverage.OutOfRange`(斜線)、該当するプロダクトがない時刻は `NotAvailable`。どちらも GFS で埋めない。
- 取得できなかった部分は `MapFrame.Issues` に入れ、取れた部分は表示する。
- 時刻の一覧(`MapAvailability`)は 2 分間使い回す。復号した GFS の格子は 16 個まで覚えておく(時間軸を行き来しても取り直さない)。

### GFS(Providers/Gfs・Grib)

| 項目 | 内容 |
|---|---|
| URL | `https://noaa-gfs-bdp-pds.s3.amazonaws.com/gfs.{yyyyMMdd}/{HH}/atmos/gfs.t{HH}z.pgrb2.0p50.f{FFF}` と `.idx` |
| 実行回 | 00/06/12/18Z。最後の予報時間(120 時間)の `.idx` があれば揃っているとみなし、なければ一つ前(最大 4 回前まで)。判定は 10 分間使い回す |
| 有効時刻 | 3 時間ごと、3〜120 時間(0 時間は降水強度がないため除く) |
| 取得 | `.idx` の行(番号:開始位置:d=初期時刻:要素:高度:予報時間)から要素の範囲を求め、Range 要求で 1 要素だけ取る(0.5° で 160〜290 KB) |
| 要素 | UGRD・VGRD(10 m above ground)、TMP(2 m above ground)、PRATE(surface、瞬間値。平均値は 6 時間ごとに区間が変わるため使わない)、TCDC(entire atmosphere、瞬間値)、PRMSL(mean sea level) |
| 単位 | K → ℃、kg/m²/s → mm/h(× 3600)、Pa → hPa。`Processing = Interpolated | UnitConverted` |
| 復号 | GRIB2 の格子テンプレート 3.0、データ表現テンプレート 5.0・5.2・5.3(GFS は 5.3)、ビットマップ。行 0 が北端になるよう並べ直す |
| 日本周辺 | 格子点ごとに `IJapanArea` で判定して NaN にする(格子の形ごとに一度だけ判定) |
| 出典 | AgencyName「NOAA」、ProductName「GFS 0.5°」、IssuedAt は初期時刻。表示は「NOAA GFS 0.5° 09:00 初期値」 |

### 気象庁の地図タイル(Providers/Jma/JmaMaps)

2026-10-04 に jmatile の設定(`bosai/{nowc,wdist,umimesh}/table/*.properties__*.xml`)と凡例の SVG から確認した。

| プロダクト | 時刻の一覧 | タイルの URL | ズーム |
|---|---|---|---|
| 雨雲の動き(nowc) | `jmatile/data/nowc/targetTimes_N1.json`(解析、過去 3 時間・5 分ごと)と `_N2`(予測、1 時間先まで) | `jmatile/data/nowc/{basetime}/none/{validtime}/surf/hrpns/{z}/{x}/{y}.png` | 偶数 4〜10 |
| 今後の雨(rasrf) | `jmatile/data/rasrf/targetTimes.json`(有効時刻ごとに最新の初期時刻を使う) | `.../rasrf/{basetime}/none/{validtime}/surf/rasrf/{z}/{x}/{y}.png` | 偶数 4〜10 |
| 天気分布予報(wdist) | `jmatile/data/wdist/targetTimes.json`(最新の初期時刻の一式。3 時間ごと翌日まで。時刻ごとに要素が違う) | `.../wdist/{basetime}/none/{validtime}/surf/{temp,wm,r3}/{z}/{x}/{y}.png` | 偶数 4〜10 |
| 海上分布予報(umimesh) | `jmatile/data/umimesh/targetTimes.json`(6 時間ごと 24 時間先まで) | 風向: `.../umimesh/{basetime}/none/{validtime}/surf/wd/data.geojson`(gzip のまま返ることがある。0.5° の点、windDir は 8 方位。海を含む区画の中心で、海岸近くの陸上の点もある)。風速: 同じ形の `.../surf/ws/data.geojson`(階級の多角形。level は `0<=kt<25`・`25<=kt<30`…`65<=kt`、陸地などは `NoData`。穴のあるリングを含む) | — |
| アメダス(全地点) | `amedas/data/latest_time.txt` | `amedas/data/map/{yyyyMMddHHmm}00.json`(日本時間。値と品質フラグ。フラグ 0 だけを使う) | — |

- 時刻は UTC の `yyyyMMddHHmmss`。存在しないタイルも 200 で透明な画像(334 バイト)が返る。
- 凡例の色(`MapLegends`): 降水強度 1/5/10/20/30/50/80 mm/h(F2F2FF・A0D2FF・218CFF・0041FF・FFF500・FF9900・FF2800・B40068)、3 時間降水量 1/5/10/15/20 mm、気温 −25〜40 ℃ の 5 ℃ ごと 15 区分(temp_point のスタイルと同じ)、天気(晴れ FFAA00・くもり AAAAAA・雨 0041FF・雨または雪 A0D2FF・雪 F2F2FF)。タイルの画素と凡例画像で数段階違う色がある(例: 250,245,0)ため、吹き出しでは近い色を許容して区分を引く。
- 海上分布予報の風速タイル(ws の PNG)は 25 kt 未満も淡い色で海域全体を塗り、地図が読めなくなるため使わない。
- 日本周辺の海上の風の流れ(2026-10-04 に利用者が決定。方針 5 により日本周辺では GFS を使えず、海岸から約 200 km の範囲に流れが出なかったため): 海上分布予報の風向(8 方位の点)と風速の階級の GeoJSON から 0.5° の区画ごとの U・V の格子(`MapFrame.JapanWind`)を作り、日本周辺(マスクの中)の粒子はこれだけで動かす。
  - **補間しない**。粒子は、いる位置に最も近い格子点(区画)の値をそのまま使う(`GridField.SampleNearest`)。気象庁の「予報業務許可に関する Q&A」は、格子点値から特定の地点の値を抜き出して空間内挿・地形補正などをすると独自の予報とみなされ許可が必要としている(2026-10-04 確認。https://www.jma.go.jp/jma/kishou/minkan/q_a_o.html )。区画ごとにそのまま描くのは、気象庁の海上分布予報の図と同じ内容の伝達にとどめるため。
  - 風速は階級の代表値(下限と上限の中央。上限がない階級は下限 + 5 kt)を m/s にしたもので、粒子の見た目の速さにだけ使い、数値・色としては表示しない。風向・風速の階級が分からない点(`NoData`・範囲外)は NaN。
  - 出典は「海上分布予報(風向・風速)」・`Processing = UnitConverted`。画面には「区画ごとに流れで表示」と出す。矢印は従来どおりそのまま描く。
  - 色(風速)は塗らない(上の風速タイルと同じ理由)。

## 出典の値

| Provider | AgencyName | ProductName | PublishingOffice | IssuedAt | License | SourceUrl |
|---|---|---|---|---|---|---|
| 気象庁 | 気象庁 | 府県天気予報 / 府県週間天気予報 / 時系列予報 / 気象警報・注意報 / アメダス | JSON の publishingOffice | reportDatetime | 公共データ利用規約(第 1.0 版)。表示「出典:気象庁ホームページ」 | 対応する気象庁ページの URL |
| NWS | National Weather Service | Forecast / Hourly Forecast / Gridpoint / Alerts / Observations | 予報官署(gridId)・senderName | updateTime / sent / timestamp | 米国政府のオープンデータ(表示文言は**要確認**) | weather.gov |
| MET | MET Norway | Locationforecast 2.0 | — | meta.updated_at | CC BY 4.0 / NLOD 2.0(改変時はその旨を表示) | https://api.met.no/ |
| GFS | NOAA | GFS 0.5° | — | 実行回の初期時刻 | Public domain (U.S. Government / NOAA) | https://registry.opendata.aws/noaa-gfs-bdp-pds/ |
| 気象庁(地図) | 気象庁 | 降水ナウキャスト / 今後の雨(降水短時間予報)/ 天気分布予報(気温・天気・降水量)/ 海上分布予報(風)/ 海上分布予報(風向・風速。流れの加工データ)/ アメダス | — | basetime(アメダスは観測時刻) | 公共データ利用規約(第 1.0 版) | 対応する気象庁の地図ページ |

## 要確認事項

- 気象庁: 時系列予報の天気区分と風速階級、週間予報の区域対応、警報の電文種別(dataTypeCode)と状態文字列、氾濫注意報のコード、アメダスの品質フラグと elems
- NWS: qualityControl コードの意味、雲量区分、ライセンス表示文言、User-Agent から API キーへの移行時期
- MET: altitude パラメータの扱い
- 地図: 気象庁の地図タイルをアプリから取得する条件(アクセス頻度)、GFS を日本以外で表示することの気象業務法上の扱い(公開前に再確認)、jmatile の URL・凡例の変更(週 1 回の実 API の契約テストで検知)

## 付録 A: 気象庁 天気コード → CompositeCondition

`Primary` / `Secondary` の表記: 降水ありは「種別(修飾)」、降水なしは「空模様+視程障害」。降水の空模様は、連続降水が Overcast、Showery が MostlyCloudy(雷雨を含む)。Intensity の既定は Moderate。

| コード | 名称 | Primary | Transition | Secondary |
|---|---|---|---|---|
| 100 | 晴 | Clear | — | — |
| 101 | 晴時々曇 | Clear | Occasionally | Overcast |
| 102 | 晴一時雨 | Clear | Temporarily | Rain(Showery) |
| 103 | 晴時々雨 | Clear | Occasionally | Rain(Showery) |
| 104 | 晴一時雪 | Clear | Temporarily | Snow(Showery) |
| 105 | 晴時々雪 | Clear | Occasionally | Snow(Showery) |
| 106 | 晴一時雨か雪 | Clear | Temporarily | RainAndSnow(Showery) |
| 107 | 晴時々雨か雪 | Clear | Occasionally | RainAndSnow(Showery) |
| 108 | 晴一時雨か雷雨 | Clear | Temporarily | Rain(Showery,Thunder) |
| 110 | 晴後時々曇 | Clear | Later | Overcast |
| 111 | 晴後曇 | Clear | Later | Overcast |
| 112 | 晴後一時雨 | Clear | Later | Rain(Showery) |
| 113 | 晴後時々雨 | Clear | Later | Rain(Showery) |
| 114 | 晴後雨 | Clear | Later | Rain |
| 115 | 晴後一時雪 | Clear | Later | Snow(Showery) |
| 116 | 晴後時々雪 | Clear | Later | Snow(Showery) |
| 117 | 晴後雪 | Clear | Later | Snow |
| 118 | 晴後雨か雪 | Clear | Later | RainAndSnow |
| 119 | 晴後雨か雷雨 | Clear | Later | Rain(Showery,Thunder) |
| 120 | 晴朝夕一時雨 | Clear | Temporarily | Rain(Showery) |
| 121 | 晴朝の内一時雨 | Clear | Temporarily | Rain(Showery) |
| 122 | 晴夕方一時雨 | Clear | Temporarily | Rain(Showery) |
| 123 | 晴山沿い雷雨 | Clear | — | — |
| 124 | 晴山沿い雪 | Clear | — | — |
| 125 | 晴午後は雷雨 | Clear | Later | Rain(Showery,Thunder) |
| 126 | 晴昼頃から雨 | Clear | Later | Rain |
| 127 | 晴夕方から雨 | Clear | Later | Rain |
| 128 | 晴夜は雨 | Clear | Later | Rain |
| 130 | 朝の内霧後晴 | Overcast+Fog | Later | Clear |
| 131 | 晴明け方霧 | Clear | Temporarily | Overcast+Fog |
| 132 | 晴朝夕曇 | Clear | Temporarily | Overcast |
| 140 | 晴時々雨で雷を伴う | Clear | Occasionally | Rain(Showery,Thunder) |
| 160 | 晴一時雪か雨 | Clear | Temporarily | RainAndSnow(Showery) |
| 170 | 晴時々雪か雨 | Clear | Occasionally | RainAndSnow(Showery) |
| 181 | 晴後雪か雨 | Clear | Later | RainAndSnow |
| 200 | 曇 | Overcast | — | — |
| 201 | 曇時々晴 | Overcast | Occasionally | Clear |
| 202 | 曇一時雨 | Overcast | Temporarily | Rain(Showery) |
| 203 | 曇時々雨 | Overcast | Occasionally | Rain(Showery) |
| 204 | 曇一時雪 | Overcast | Temporarily | Snow(Showery) |
| 205 | 曇時々雪 | Overcast | Occasionally | Snow(Showery) |
| 206 | 曇一時雨か雪 | Overcast | Temporarily | RainAndSnow(Showery) |
| 207 | 曇時々雨か雪 | Overcast | Occasionally | RainAndSnow(Showery) |
| 208 | 曇一時雨か雷雨 | Overcast | Temporarily | Rain(Showery,Thunder) |
| 209 | 霧 | Overcast+Fog | — | — |
| 210 | 曇後時々晴 | Overcast | Later | Clear |
| 211 | 曇後晴 | Overcast | Later | Clear |
| 212 | 曇後一時雨 | Overcast | Later | Rain(Showery) |
| 213 | 曇後時々雨 | Overcast | Later | Rain(Showery) |
| 214 | 曇後雨 | Overcast | Later | Rain |
| 215 | 曇後一時雪 | Overcast | Later | Snow(Showery) |
| 216 | 曇後時々雪 | Overcast | Later | Snow(Showery) |
| 217 | 曇後雪 | Overcast | Later | Snow |
| 218 | 曇後雨か雪 | Overcast | Later | RainAndSnow |
| 219 | 曇後雨か雷雨 | Overcast | Later | Rain(Showery,Thunder) |
| 220 | 曇朝夕一時雨 | Overcast | Temporarily | Rain(Showery) |
| 221 | 曇朝の内一時雨 | Overcast | Temporarily | Rain(Showery) |
| 222 | 曇夕方一時雨 | Overcast | Temporarily | Rain(Showery) |
| 223 | 曇日中時々晴 | Overcast | Occasionally | Clear |
| 224 | 曇昼頃から雨 | Overcast | Later | Rain |
| 225 | 曇夕方から雨 | Overcast | Later | Rain |
| 226 | 曇夜は雨 | Overcast | Later | Rain |
| 228 | 曇昼頃から雪 | Overcast | Later | Snow |
| 229 | 曇夕方から雪 | Overcast | Later | Snow |
| 230 | 曇夜は雪 | Overcast | Later | Snow |
| 231 | 曇海上海岸は霧か霧雨 | Overcast | — | — |
| 240 | 曇時々雨で雷を伴う | Overcast | Occasionally | Rain(Showery,Thunder) |
| 250 | 曇時々雪で雷を伴う | Overcast | Occasionally | Snow(Showery,Thunder) |
| 260 | 曇一時雪か雨 | Overcast | Temporarily | RainAndSnow(Showery) |
| 270 | 曇時々雪か雨 | Overcast | Occasionally | RainAndSnow(Showery) |
| 281 | 曇後雪か雨 | Overcast | Later | RainAndSnow |
| 300 | 雨 | Rain | — | — |
| 301 | 雨時々晴 | Rain | Occasionally | Clear |
| 302 | 雨時々止む | Rain(Showery) | — | — |
| 303 | 雨時々雪 | Rain | Occasionally | Snow(Showery) |
| 304 | 雨か雪 | RainAndSnow | — | — |
| 306 | 大雨 | Rain(Heavy) | — | — |
| 308 | 雨で暴風を伴う | Rain(Heavy,StrongWind) | — | — |
| 309 | 雨一時雪 | Rain | Temporarily | Snow(Showery) |
| 311 | 雨後晴 | Rain | Later | Clear |
| 313 | 雨後曇 | Rain | Later | Overcast |
| 314 | 雨後時々雪 | Rain | Later | Snow(Showery) |
| 315 | 雨後雪 | Rain | Later | Snow |
| 316 | 雨か雪後晴 | RainAndSnow | Later | Clear |
| 317 | 雨か雪後曇 | RainAndSnow | Later | Overcast |
| 320 | 朝の内雨後晴 | Rain | Later | Clear |
| 321 | 朝の内雨後曇 | Rain | Later | Overcast |
| 322 | 雨朝晩一時雪 | Rain | Temporarily | Snow(Showery) |
| 323 | 雨昼頃から晴 | Rain | Later | Clear |
| 324 | 雨夕方から晴 | Rain | Later | Clear |
| 325 | 雨夜は晴 | Rain | Later | Clear |
| 326 | 雨夕方から雪 | Rain | Later | Snow |
| 327 | 雨夜は雪 | Rain | Later | Snow |
| 328 | 雨一時強く降る | Rain | Temporarily | Rain(Heavy) |
| 329 | 雨一時みぞれ | Rain | Temporarily | RainAndSnow(Showery) |
| 340 | 雪か雨 | RainAndSnow | — | — |
| 350 | 雨で雷を伴う | Rain(Thunder) | — | — |
| 361 | 雪か雨後晴 | RainAndSnow | Later | Clear |
| 371 | 雪か雨後曇 | RainAndSnow | Later | Overcast |
| 400 | 雪 | Snow | — | — |
| 401 | 雪時々晴 | Snow | Occasionally | Clear |
| 402 | 雪時々止む | Snow(Showery) | — | — |
| 403 | 雪時々雨 | Snow | Occasionally | Rain(Showery) |
| 405 | 大雪 | Snow(Heavy) | — | — |
| 406 | 風雪強い | Snow(StrongWind) | — | — |
| 407 | 暴風雪 | Snow(Heavy,StrongWind) | — | — |
| 409 | 雪一時雨 | Snow | Temporarily | Rain(Showery) |
| 411 | 雪後晴 | Snow | Later | Clear |
| 413 | 雪後曇 | Snow | Later | Overcast |
| 414 | 雪後雨 | Snow | Later | Rain |
| 420 | 朝の内雪後晴 | Snow | Later | Clear |
| 421 | 朝の内雪後曇 | Snow | Later | Overcast |
| 422 | 雪昼頃から雨 | Snow | Later | Rain |
| 423 | 雪夕方から雨 | Snow | Later | Rain |
| 425 | 雪一時強く降る | Snow | Temporarily | Snow(Heavy) |
| 426 | 雪後みぞれ | Snow | Later | RainAndSnow |
| 427 | 雪一時みぞれ | Snow | Temporarily | RainAndSnow(Showery) |
| 450 | 雪で雷を伴う | Snow(Thunder) | — | — |

## 付録 B: NWS グリッド weather → WeatherCondition

enum は NWS OpenAPI 3.11.0 の Gridpoint.weather 定義による(2026-10-04 取得)。

| weather | PrecipitationType | 付加 |
|---|---|---|
| rain | Rain | — |
| rain_showers | Rain | Showery |
| snow | Snow | — |
| snow_showers | Snow | Showery |
| sleet | IcePellets | — |
| freezing_rain | FreezingRain | — |
| freezing_drizzle | FreezingRain | Intensity=Light |
| drizzle | Drizzle | — |
| hail | Hail | — |
| thunderstorms | Rain | Showery, Thunder |
| fog / freezing_fog / ice_fog | — | Obscuration=Fog |
| haze | — | Obscuration=Haze |
| smoke | — | Obscuration=Smoke |
| blowing_dust / blowing_sand / volcanic_ash | — | Obscuration=Dust |
| blowing_snow | — | StrongWind(降雪とは扱わない) |
| frost / ice_crystals / freezing_spray / water_spouts | — | 反映しない |

同一区間に複数の weather がある場合は、強度は最大、フラグは論理和、降水種別は Hail > FreezingRain > IcePellets > Snow > Rain > Drizzle の優先で 1 つにする。

## 付録 C: 気象庁 警報コード(r8)

気象庁 警報ページの JS から抽出(2026-10-04)。level 20 = 注意報、30 = 警報、40 = 危険警報、50 = 特別警報。WarningLevel は大雨・土砂災害・氾濫・高潮のみ。氾濫は 30/31、40/41、51/53 の 2 系統があり意味は**要確認**。

| コード | elem | 名称 | Tier | WarningLevel |
|---|---|---|---|---|
| 02 | wind_snow | 暴風雪警報 | Warning | — |
| 03 | rain | レベル3大雨警報 | Warning | 3 |
| 05 | wind | 暴風警報 | Warning | — |
| 06 | snow | 大雪警報 | Warning | — |
| 07 | wave | 波浪警報 | Warning | — |
| 08 | tide | レベル3高潮警報 | Warning | 3 |
| 09 | landslide | レベル3土砂災害警報 | Warning | 3 |
| 10 | rain | レベル2大雨注意報 | Advisory | 2 |
| 12 | snow | 大雪注意報 | Advisory | — |
| 13 | wind_snow | 風雪注意報 | Advisory | — |
| 14 | thunder | 雷注意報 | Advisory | — |
| 15 | wind | 強風注意報 | Advisory | — |
| 16 | wave | 波浪注意報 | Advisory | — |
| 17 | snow_melting | 融雪注意報 | Advisory | — |
| 19 | tide | レベル2高潮注意報 | Advisory | 2 |
| 20 | fog | 濃霧注意報 | Advisory | — |
| 21 | dry | 乾燥注意報 | Advisory | — |
| 22 | avalanche | なだれ注意報 | Advisory | — |
| 23 | cold | 低温注意報 | Advisory | — |
| 24 | frost | 霜注意報 | Advisory | — |
| 25 | ice_accretion | 着氷注意報 | Advisory | — |
| 26 | snow_accretion | 着雪注意報 | Advisory | — |
| 29 | landslide | レベル2土砂災害注意報 | Advisory | 2 |
| 30 | flood | レベル3氾濫警報 | Warning | 3 |
| 31 | flood | レベル3氾濫警報 | Warning | 3 |
| 32 | wind_snow | 暴風雪特別警報 | Emergency | — |
| 33 | rain | レベル5大雨特別警報 | Emergency | 5 |
| 35 | wind | 暴風特別警報 | Emergency | — |
| 36 | snow | 大雪特別警報 | Emergency | — |
| 37 | wave | 波浪特別警報 | Emergency | — |
| 38 | tide | レベル5高潮特別警報 | Emergency | 5 |
| 39 | landslide | レベル5土砂災害特別警報 | Emergency | 5 |
| 40 | flood | レベル4氾濫危険警報 | Danger | 4 |
| 41 | flood | レベル4氾濫危険警報 | Danger | 4 |
| 43 | rain | レベル4大雨危険警報 | Danger | 4 |
| 48 | tide | レベル4高潮危険警報 | Danger | 4 |
| 49 | landslide | レベル4土砂災害危険警報 | Danger | 4 |
| 51 | flood | レベル5氾濫特別警報 | Emergency | 5 |
| 53 | flood | レベル5氾濫特別警報 | Emergency | 5 |

## 付録 D: MET Norway シンボル → WeatherCondition

シンボル一覧は metno/weathericons の legend.csv(41 件)による。表は `_day` / `_night` / `_polartwilight` 除去後のコード。

| シンボル | Sky | Precipitation | Intensity | Showery | Thunder |
|---|---|---|---|---|---|
| clearsky | Clear | — | — | — | — |
| fair | MostlyClear | — | — | — | — |
| partlycloudy | PartlyCloudy | — | — | — | — |
| cloudy | Overcast | — | — | — | — |
| lightrainshowers | PartlyCloudy | Rain | Light | ✓ | — |
| rainshowers | PartlyCloudy | Rain | Moderate | ✓ | — |
| heavyrainshowers | PartlyCloudy | Rain | Heavy | ✓ | — |
| lightrainshowersandthunder | PartlyCloudy | Rain | Light | ✓ | ✓ |
| rainshowersandthunder | PartlyCloudy | Rain | Moderate | ✓ | ✓ |
| heavyrainshowersandthunder | PartlyCloudy | Rain | Heavy | ✓ | ✓ |
| lightsleetshowers | PartlyCloudy | RainAndSnow | Light | ✓ | — |
| sleetshowers | PartlyCloudy | RainAndSnow | Moderate | ✓ | — |
| heavysleetshowers | PartlyCloudy | RainAndSnow | Heavy | ✓ | — |
| lightssleetshowersandthunder | PartlyCloudy | RainAndSnow | Light | ✓ | ✓ |
| sleetshowersandthunder | PartlyCloudy | RainAndSnow | Moderate | ✓ | ✓ |
| heavysleetshowersandthunder | PartlyCloudy | RainAndSnow | Heavy | ✓ | ✓ |
| lightsnowshowers | PartlyCloudy | Snow | Light | ✓ | — |
| snowshowers | PartlyCloudy | Snow | Moderate | ✓ | — |
| heavysnowshowers | PartlyCloudy | Snow | Heavy | ✓ | — |
| lightssnowshowersandthunder | PartlyCloudy | Snow | Light | ✓ | ✓ |
| snowshowersandthunder | PartlyCloudy | Snow | Moderate | ✓ | ✓ |
| heavysnowshowersandthunder | PartlyCloudy | Snow | Heavy | ✓ | ✓ |
| lightrain | Overcast | Rain | Light | — | — |
| rain | Overcast | Rain | Moderate | — | — |
| heavyrain | Overcast | Rain | Heavy | — | — |
| lightrainandthunder | Overcast | Rain | Light | — | ✓ |
| rainandthunder | Overcast | Rain | Moderate | — | ✓ |
| heavyrainandthunder | Overcast | Rain | Heavy | — | ✓ |
| lightsleet | Overcast | RainAndSnow | Light | — | — |
| sleet | Overcast | RainAndSnow | Moderate | — | — |
| heavysleet | Overcast | RainAndSnow | Heavy | — | — |
| lightsleetandthunder | Overcast | RainAndSnow | Light | — | ✓ |
| sleetandthunder | Overcast | RainAndSnow | Moderate | — | ✓ |
| heavysleetandthunder | Overcast | RainAndSnow | Heavy | — | ✓ |
| lightsnow | Overcast | Snow | Light | — | — |
| snow | Overcast | Snow | Moderate | — | — |
| heavysnow | Overcast | Snow | Heavy | — | — |
| lightsnowandthunder | Overcast | Snow | Light | — | ✓ |
| snowandthunder | Overcast | Snow | Moderate | — | ✓ |
| heavysnowandthunder | Overcast | Snow | Heavy | — | ✓ |
| fog | Overcast+Fog | — | — | — | — |
