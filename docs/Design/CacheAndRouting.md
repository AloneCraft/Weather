# キャッシュ層・国判定・Provider 振り分け

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| 日本周辺の扱い | 気象庁の市町村区域(class20)内 → その区域。区域外でも 20 km 以内 → 最寄り区域を「最寄り」と明示して表示。それ以外の日本周辺域(沖合の海上・北方領土等)→「対象外」。MET / NWS には振り分けない | 方針 5。気象庁サイト自身の現在地判定も「区域内でなければ最寄り」を採る |
| 座標の丸め | MET / NWS へのリクエストは小数 2 桁(約 1 km)に丸める。気象庁へは座標を送らない(地域コードのみ) | 規約上限(4 桁)以内。プライバシーとキャッシュ効率。予報の解像度(MET 約 1 km、NWS 2.5 km)に対して十分 |
| HTTP キャッシュの保存先 | ファイル(`FileSystem.CacheDirectory/http/`) | OS が容量逼迫時に消してよい性質のデータ。実装と削除が単純 |
| 履歴・お気に入りの保存先 | SQLite(Microsoft.Data.Sqlite、手書き SQL)、`FileSystem.AppDataDirectory/weather.db` | 永続データ。sqlite-net-pcl はリフレクション ORM でトリミング/AOT 警告のリスク、EF Core は重い。Microsoft.Data.Sqlite の AOT 警告有無は実装時に**要確認** |
| 地点検索・逆ジオコーディング | すべてオフライン。日本 = 気象庁 area.json + class20 ポリゴン、世界 = GeoNames cities5000 + 日本語別名、国境 = Natural Earth 10m admin 0(日本の視点版) | ユーザー要望(オフライン)。プラットフォームのジオコーダー(MAUI Geocoding)はオンライン・提供元依存のため使わない |
| タイムゾーン | 日本 = Asia/Tokyo、NWS 地点 = points の timeZone、その他 = 最寄り GeoNames 都市の timezone | オフラインで十分な精度。タイムゾーン境界付近の誤差は既知の制約 |

## 観測事実(2026-10-04)

| 項目 | 結果 |
|---|---|
| キャッシュヘッダー | 気象庁 forecast / warning / amedas: `Cache-Control: max-age=60`+Last-Modified+ETag(Expires なし)、時系列予報: max-age=300 / MET: Expires+Last-Modified(Expires まで約 30 分) / NWS points: `public, max-age=86400, s-maxage=120`+Expires |
| NWS の対応範囲 | /points が 200: 米国本土・ホノルル・アンカレッジ・グアム・プエルトリコ。404: 太平洋上・バンクーバー |
| 座標精度 | NWS は 5 桁で 301(4 桁へリダイレクト)。MET は 5 桁でも 200 だが規約上は 4 桁まで |
| 気象庁 class20 | `common/const/class20relm.json`(1805 区域の外接矩形、144 KB)と `common/const/geojson/class20s/{code}.json`(区域ポリゴン)。北方領土の区域はない。竹島は隠岐の島町のポリゴンに含まれ、尖閣は石垣市のポリゴンから約 1 km(地理データ生成後の解決結果で確認) |
| GeoNames | cities15000 3.4 MB / cities5000 5.7 MB / cities1000 11 MB(zip)。alternateNamesV2 は 205 MB(ビルド時に日本語分だけ抽出) |
| Natural Earth | `ne_10m_admin_0_countries_jpn.zip` 4.9 MB(日本の視点版)、50m 版 0.8 MB |

## 全体の流れ

```
UI(Presentation)
  → IWeatherService(Providers)
      → ILocationResolver(Core 抽象 / Geo 実装): GeoPoint → ResolvedLocation
      → WeatherProviderRouter(Providers): ResolvedLocation → Provider
      → IForecastProvider / IAlertProvider / IObservationProvider
          → HttpClient: UserAgentHandler → HttpCacheHandler → ネイティブハンドラ
                                            ↘ IHttpCacheStore(Core 抽象 / Infrastructure 実装)
```

`IWeatherService` は Core の抽象のみに依存するため、Functions 移行時はサーバー側で同じ実装を動かし、クライアントは RemoteWeatherService に差し替える。

## 地点の解決

```csharp
namespace Weather.Core;

public readonly record struct GeoPoint{
    public GeoPoint(double latitude,double longitude){
        if(latitude is <-90 or >90){
            throw new ArgumentOutOfRangeException(nameof(latitude));
        }
        if(longitude is <-180 or >180){
            throw new ArgumentOutOfRangeException(nameof(longitude));
        }
        this.Latitude=latitude;
        this.Longitude=longitude;
    }

    public double Latitude{get;}
    public double Longitude{get;}

    public GeoPoint RoundForRequest(){
        return new GeoPoint(Math.Round(this.Latitude,2),Math.Round(this.Longitude,2));
    }
}

public enum JmaAreaMatchKind{Inside,Nearest,OutOfCoverage}

public sealed record JmaAreaMatch(JmaAreaMatchKind Kind,string? Class20Code,double DistanceKm);

public sealed record ResolvedLocation{
    public required GeoPoint Point{get;init;}          //リクエスト用に丸め済み
    public required string CountryCode{get;init;}      //ISO 3166-1 alpha-2。公海は "XX"
    public required string DisplayName{get;init;}
    public required string TimeZoneId{get;init;}       //IANA
    public string? AdminName{get;init;}                //都道府県・州
    public JmaAreaMatch? JmaArea{get;init;}            //日本周辺域なら非 null
}

public interface ILocationResolver{
    ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken);
}
```

### 解決手順(Geo)

1. **日本周辺域の判定**: ビルド時に作る「日本周辺域マスク」(0.05° 格子のビット列)を引く。マスクは「日本(Natural Earth 日本視点版)の陸地、および最寄りの陸地が日本である 200 km 以内の海域」。
2. **日本周辺域なら気象庁区域を判定**:
   - class20 外接矩形で候補を絞り、ポリゴン内判定 → 内側なら `Inside`。
   - 内側でなく、最寄り区域ポリゴンまでの距離が 20 km 以内 → `Nearest`。
   - それ以外 → `OutOfCoverage`(北方領土・沖合の海上など)。竹島・尖閣は気象庁の区域定義(隠岐の島町・石垣市)に従ってその区域になる。
   - いずれも `CountryCode = "JP"`、`TimeZoneId = "Asia/Tokyo"`。
3. **日本周辺域でなければ国判定**: Natural Earth 国境ポリゴン(外接矩形索引+ポリゴン内判定)。ISO_A2 が `-99` の国があるため ISO_A2_EH を使う(**要確認**)。どの国にも入らなければ `"XX"`。
4. **表示名**: 日本は class20 名+都道府県名。その他は 50 km 以内の最寄り GeoNames 都市「都市, 州, 国」、なければ座標表記。
5. **タイムゾーン**: 最寄り GeoNames 都市の timezone。NWS Adapter は points の timeZone で上書きする。

## 振り分け

| 条件 | 予報 | 警報 | 観測 |
|---|---|---|---|
| `JmaArea.Kind` が Inside / Nearest | 気象庁 | 気象庁 | 気象庁(最寄りアメダス) |
| `JmaArea.Kind` が OutOfCoverage | 対象外 | 対象外 | 対象外 |
| 国コード US / PR / GU / VI / AS / MP | NWS(404 なら MET) | NWS(404 なら非対応) | NWS |
| その他 | MET | 非対応 | 非対応 |

- **不変条件**: 日本周辺域(`JmaArea` 非 null)では MET / NWS を一切選ばない。代替(フォールバック)も禁止。
- NWS → MET のフォールバックは **NotFound(グリッド範囲外)のときだけ**。通信障害時に Provider を切り替えると出典が黙って変わるため、通信障害はキャッシュ表示で扱う。
- VI / AS / MP の NWS 対応は**要確認**(フォールバックがあるため振り分け自体は安全)。

```csharp
namespace Weather.Providers;

public enum RouteReason{Jma,JapanOutOfCoverage,Nws,MetNorway}

public sealed record ForecastRoute(IForecastProvider? Primary,IForecastProvider? FallbackOnNotFound,RouteReason Reason);

public sealed class WeatherProviderRouter(IEnumerable<IForecastProvider> forecastProviders){
    private static readonly FrozenSet<string> NwsCountries=new[]{"US","PR","GU","VI","AS","MP"}.ToFrozenSet();
    private readonly FrozenDictionary<ProviderId,IForecastProvider> forecasts=forecastProviders.ToFrozenDictionary(static p=>p.Id);

    public ForecastRoute RouteForecast(ResolvedLocation location){
        if(location.JmaArea is not null){
            if(location.JmaArea.Kind==JmaAreaMatchKind.OutOfCoverage){
                return new ForecastRoute(null,null,RouteReason.JapanOutOfCoverage);
            }
            return new ForecastRoute(this.forecasts[ProviderId.Jma],null,RouteReason.Jma);
        }
        if(NwsCountries.Contains(location.CountryCode)){
            return new ForecastRoute(this.forecasts[ProviderId.Nws],this.forecasts[ProviderId.MetNorway],RouteReason.Nws);
        }
        return new ForecastRoute(this.forecasts[ProviderId.MetNorway],null,RouteReason.MetNorway);
    }
    // RouteAlerts / RouteObservations も同じ規則
}
```

## HTTP キャッシュ

### 構成

- `HttpCacheHandler`(Providers、DelegatingHandler)が鮮度判定・条件付き GET・期限切れ時の代替表示を担う。
- `IHttpCacheStore`(Core 抽象)を `FileHttpCacheStore`(Infrastructure)が実装する。Functions 移行時は Blob 等の実装に差し替える。
- 時刻は `TimeProvider` から取得する(テストで FakeTimeProvider を使う)。

### エントリ

キー = メソッド+正規化 URL(丸め済み座標)。保存する値: ステータス、本文、Content-Type、ETag、Last-Modified(**受信した文字列のまま**)、Date、Age、max-age、Expires、受信時刻、最終利用時刻。

### 鮮度(RFC 9111 を簡略化)

- 鮮度寿命 = `max-age`(私的キャッシュのため `s-maxage` は無視)、なければ `Expires − Date`、どちらもなければ 0。
- 現在の経過時間 = `Age` ヘッダー + (現在 − 受信時刻)。
- 例: NWS points は max-age=86400 なので 1 日、MET は Expires まで、気象庁は 60 秒(時系列予報は 300 秒)。

### 処理

1. **新鮮** → ネットワークに出ずキャッシュを返す。
2. **期限切れ** → 条件付き GET(`If-None-Match` = ETag、`If-Modified-Since` = 保存した Last-Modified 文字列)。304 ならメタデータを更新してキャッシュ本文を返す。200 なら保存する。
3. **通信失敗・タイムアウト・5xx** → 許容期間内の古いエントリがあれば返し、レスポンスに印を付ける。Adapter は `SourceAttribution.IsStale = true` にする。許容期間: 予報 7 日、警報 6 時間(古い警報は危険なため短くする)、観測 7 日、定義データ 無期限。
4. 同じキーの同時リクエストは 1 本にまとめる(single-flight)。

### アプリ側の更新方針(HTTP 鮮度の上に重ねる)

- 更新のきっかけ: アプリが前面に来たとき、地点の切り替え、引っ張って更新、表示中の自動更新(10 分ごと)。
- MET は Expires 前の再取得をキャッシュ層が止めるため、自動更新を増やしても規約違反にならない。
- **バックグラウンド取得はしない**(MET 規約。気象庁・NWS も MVP では行わない)。
- 定義データ(area.json、forecast_area.json、amedastable.json)は、アプリ同梱の版を初期値とし、7 日に 1 回だけ再検証する。

### 容量管理

上限 50 MB。超えたら最終利用時刻の古い順に削除する。14 日使われていないエントリは起動時に削除する。iOS は容量逼迫時に CacheDirectory を消すことがあるが、許容する。

### HTTP エラーの変換

| 状況 | ProviderFailure | 備考 |
|---|---|---|
| 403 | Forbidden | User-Agent 不備の可能性。診断ログに記録 |
| 429 | RateLimited | Retry-After を尊重し、自動リトライはしない |
| 404 | NotFound | NWS では範囲外の意味 |
| 5xx | ServerError | |
| タイムアウト(15 秒) | Timeout | |
| 通信不能 | Network | |
| JSON 不正・必須項目欠落 | InvalidResponse | |

## 地理データ(オフライン)

`tools/Weather.GeoDataBuilder`(net10.0 コンソール、配布しない)が元データを取得・加工し、`src/Weather.Geo/Data/` に埋め込みリソースとして出力する。

| 成果物 | 元データ | 内容 | ライセンス |
|---|---|---|---|
| jp-areas.bin | 気象庁 area.json、class20relm.json、class20 ポリゴン | 区域コード・名称・かな・階層・外接矩形・ポリゴン | 公共データ利用規約(第 1.0 版)、出典表示 |
| jp-area-mask.bin | Natural Earth 10m(日本視点版)+ class20 | 日本周辺域マスク(0.05° 格子) | パブリックドメイン+上記 |
| countries.bin | Natural Earth 10m admin 0(日本視点版) | 簡略化した国境ポリゴン+ISO コード | パブリックドメイン |
| places.bin | GeoNames cities5000、admin1CodesASCII、alternateNamesV2(ja) | 名称・ASCII 名・日本語名・座標・国・州・人口・timezone | CC BY 4.0(クレジット表示) |

- 合計サイズの目標は 15 MB 以下。2026-10-04 の生成結果は約 4.3 MB(jp-areas 0.7 MB / countries 1.2 MB / jp-mask 3 KB / places 2.4 MB、都市 69,763 件)。
- 埋め込みリソースにする理由: Functions でも同じアセンブリで使えるため。
- 読み込みは遅延させる(初回の検索・解決時)。
- データの版を記録し、再生成の手順を tools の README に書く。

### 地点検索

- 正規化(NFKC、小文字化、カタカナ → ひらがな)したキーで前方一致検索する。
- 日本の市町村は気象庁 area.json の名称・かなを使う。世界は GeoNames の名称・ASCII 名・日本語名を使う。
- 並び順は「完全一致 > 前方一致」、次に人口順。入力が日本語なら日本の区域を優先する。

## 不変条件

1. 日本周辺域の地点に MET / NWS を割り当てない(Router のテスト+Forecast コンストラクタの検証の二重化)
2. MET / NWS へのリクエスト座標は小数 2 桁以下
3. MET への If-Modified-Since は、直前の Last-Modified と完全に同じ文字列
4. 鮮度内のエントリがあるとき、ネットワークへ出ない
5. 古いキャッシュを表示するときは必ず `IsStale` を立て、UI に取得時刻を示す
6. バックグラウンドで MET を取得しない

## 要確認事項

- Natural Earth の ISO_A2_EH の扱い、VI / AS / MP の NWS 対応
- 地理データの合計サイズ
- Microsoft.Data.Sqlite の AOT / トリミング警告
