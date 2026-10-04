# ソリューション・プロジェクト構成

## 前提

- .NET 10(最新 LTS)/ C# 14。.NET 11 は STS のため採用しない。
- iOS のビルド・署名には Mac + Xcode が必要。Mac は利用可能(Pair to Mac / ローカルビルド)。
- Azure Functions(Phase 4 で Weather.Functions として実装、未配置)は isolated worker モデル(in-process モデルは 2026 年 11 月サポート終了予定)。Microsoft.Azure.Functions.Worker 2.52.0 / Worker.Sdk 2.1.0 / Extensions.Http 3.3.0 で net10.0 のビルドを確認(2026-10-04)。Azure 側のランタイムの対応は配置時に**要確認**。

## 構成

```
Weather/
├─ Weather.slnx
├─ Directory.Build.props        共通設定(Nullable、解析レベル等)
├─ Directory.Packages.props     Central Package Management(SkiaSharp 等のバージョンを一元固定)
├─ docs/Design/                 合意済み設計書
├─ src/
│  ├─ Weather.Core            net10.0
│  ├─ Weather.Providers       net10.0
│  ├─ Weather.Geo             net10.0
│  ├─ Weather.Scene           net10.0
│  ├─ Weather.Rendering       net10.0  (SkiaSharp)
│  ├─ Weather.Infrastructure  net10.0
│  ├─ Weather.Presentation    net10.0  (CommunityToolkit.Mvvm)
│  └─ Weather.App             net10.0-android;net10.0-ios  (MAUI)
├─ tools/
│  ├─ Weather.SceneLab        net10.0-windows  (WPF + SkiaSharp、描画調整用・非配布)
│  └─ Weather.GeoDataBuilder  net10.0  (地理データの生成・非配布)
└─ tests/
   ├─ Weather.Core.Tests
   ├─ Weather.Providers.Tests
   ├─ Weather.Providers.LiveTests   (実 API の契約テスト。PR では実行しない)
   ├─ Weather.Geo.Tests
   ├─ Weather.Scene.Tests
   ├─ Weather.Rendering.Tests
   ├─ Weather.Infrastructure.Tests
   └─ Weather.Presentation.Tests
```

各ライブラリの csproj は TargetFramework を個別に宣言する(Directory.Build.props で TargetFramework を設定すると App のマルチターゲットが無効になるため)。

## 各プロジェクトの責務

| プロジェクト | 責務 | 参照 | Functions 移行時 |
|---|---|---|---|
| Core | ドメインモデル(ForecastPoint 等)、抽象(IWeatherProvider、ILocationResolver、キャッシュストア)、値型(GeoPoint 等) | なし | 再利用 |
| Providers | 気象庁・NWS・MET Norway の Adapter、HTTP 共通処理(User-Agent、条件付き GET、Expires 解釈、Range 要求)、Provider ルーター、JSON の読み取り(JsonDocument)。地図のデータ(GFS・GRIB2 の復号・気象庁の地図タイル・MapDataService) | Core | 再利用(サーバー側で実行) |
| Geo | オフライン地点検索・逆ジオコーディング・国判定・タイムゾーン解決 | Core | 再利用(国判定)。クライアントの地点検索でも継続使用 |
| Scene | 予報データ → SceneState の変換(純粋ロジック) | Core | 不要 |
| Rendering | SceneState → SkiaSharp 描画、グラフ、地図(Web メルカトル・格子・タイル・粒子)。MAUI 非依存 | Scene, Geo | 不要(Web 版で再利用可能) |
| Infrastructure | 端末側永続化(キャッシュストア・履歴ストアの実装) | Core | Blob / Table 等に置き換え |
| Presentation | ViewModel、画面状態、ナビゲーション・ダイアログ・位置情報・Dispatcher の薄い抽象 | Core, Scene | 不要 |
| App | MAUI ホスト、XAML View、DI 合成ルート、プラットフォーム実装(位置情報・保存パス・接続状態・通知・ウィジェット・背景更新・広告・課金)、描画ホスト(Android は SKGLView、iOS は SKMetalView の自作ハンドラー) | すべて(Remote を除く) | `AddRemoteWeatherService` で RemoteWeatherService に差し替え |
| Remote | Functions とアプリの間の電文(DTO)と変換、サーバー側の要求処理(WeatherApi。Functions のホストに依存しない)、クライアント(RemoteWeatherService) | Core | Phase 4 で実装済み。既定のアプリでは使わない(方針 2) |
| Functions | Azure Functions(isolated worker)の HTTP API。resolve / forecast / alerts / stations / observations を WeatherApi に渡すだけ | Core, Providers, Geo, Remote | Phase 4 で実装済み(未配置) |
| Widget.iOS | iOS のウィジェット(WidgetKit 拡張。Swift。XcodeGen で生成)。App Group の widget.json を表示するだけ | なし(JSON の形のみ Presentation と共有) | 不要 |
| SceneLab | SceneState をスライダーで操作し描画を即時プレビューする開発ツール | Scene, Rendering | — |
| GeoDataBuilder | 気象庁・GeoNames・Natural Earth から地理データを生成し、Geo の埋め込みリソースに出力する | Geo | — |

依存方向:

- Providers / Geo / Scene / Infrastructure → Core
- Rendering → Scene, Geo(地図で国境・区域・地名・日本周辺域のマスクを使うため)
- Presentation → Core, Scene
- App → すべて(Remote を除く)
- Remote → Core(アーキテクチャテストで検査)
- Functions → Core, Providers, Geo, Remote
- SceneLab → Scene, Rendering
- GeoDataBuilder → Geo

## 設計判断と根拠

### ロジックは App の外に置く
App プロジェクトはプラットフォーム TFM(net10.0-android 等)のみを持つため、通常の net10.0 テストプロジェクトから参照できず、テストにはエミュレーターが必要になる。ロジックを net10.0 ライブラリに置くことで Windows 上の `dotnet test` で検証でき、Functions 移行・Web 版にもそのまま流用できる。

### ViewModel は Presentation に分離
- 利点: MAUI ランタイムなしで VM をテストできる。VM への MAUI API 混入をコンパイル時に防げる。
- 代償: ナビゲーション・ダイアログ・位置情報・Dispatcher 等に薄い抽象が必要。MAUI のサンプル(App 内 VM 前提)と形が異なる。

### Providers は単一プロジェクト(Provider ごとに名前空間分割)
HTTP・キャッシュヘッダ処理・例外変換の共通基盤を internal で共有でき、Functions でも 3 Provider は同一ホストで動く。Provider は 3 つで固定のため、プロジェクト分割は過剰と判断。

### Scene と Rendering を分離
シーン変換のテストに SkiaSharp ネイティブライブラリを持ち込まない。Rendering も MAUI 非依存とし、App は Skia の canvas を渡すだけにする。同じ描画コードを SceneLab と将来の Web 版で使える。

### Geo を独立
データのサイズ・ライセンスの性質が他と異なる。Providers は Geo を直接参照せず Core の ILocationResolver 経由で使うため、国判定の実装を端末/サーバーで差し替えられる。MET Norway は時刻を UTC で返すため、現地時刻表示に必要なタイムゾーン解決もここが担う。

### Infrastructure を分離
SQLite 系ライブラリは net10.0 で iOS / Android でも動くため、デスクトップで実ファイルを使ったテストができる。保存パス等の MAUI API(FileSystem.AppDataDirectory)は App から注入する。

### 描画開発は SceneLab で行う
Android エミュレーターは GPU 性能が実機と異なり、調整のたびにデプロイ待ちが発生する。描画の試行錯誤は Windows デスクトップの SceneLab で行い、App のターゲットは iOS / Android のみに保つ。

## MAUI 固有の注意点

- iOS は JIT 不可(AOT)で、リリースビルドではトリミングが既定で有効。リフレクション依存は「実機リリースビルドでのみ壊れる」典型的な不具合になるため、共有ライブラリで IsAotCompatible を有効にして解析器を早期に効かせ、JSON はリフレクションを使わない方法(API 応答の読み取りは JsonDocument、保存するデータは source generator)に統一する。
- SkiaSharp のバージョン不一致はネイティブ部分の不整合を起こしやすい。SkiaSharp 4.x(2026-10-04 時点の安定版 4.153.1)を Central Package Management で一元固定する。SkiaSharp 3.x に依存するライブラリ(LiveCharts2 2.0.5、Mapsui 5.1)は使わず、グラフと地図は Rendering で自前描画する(Rendering.md)。
- MAUI の DI は Microsoft.Extensions.DependencyInjection そのもの。各ライブラリが公開する `AddXxx(this IServiceCollection)` を Functions(isolated)でも再利用する。

## 不変条件・ガードレール

文章だけでなく、テスト・解析器で強制する。

1. App 以外のプロジェクトは `Microsoft.Maui.*` を参照しない(参照アセンブリ検査テスト)
2. Providers は Geo を参照しない(同上)
3. 共有ライブラリは `IsAotCompatible=true`。JSON はリフレクションに依存しない(API 応答は JsonDocument、保存データは source generator)
4. 現在時刻は `TimeProvider` を注入して取得する。`DateTime.Now` / `DateTime.UtcNow` は BannedApiAnalyzers で禁止する
5. `HttpClient` は `IHttpClientFactory` 経由。User-Agent は Provider 共通ハンドラで必ず付与する
6. 各ライブラリは `AddXxx(IServiceCollection)` を公開し、MAUI と Functions で同じ登録を再利用する

```csharp
public class ArchitectureRules{
    [Theory]
    [InlineData(typeof(Weather.Core.GeoPoint))]
    [InlineData(typeof(Weather.Providers.WeatherProviderRouter))]
    [InlineData(typeof(Weather.Scene.SceneState))]
    public void NoMauiReference(Type marker){
        var names=marker.Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"");
        Assert.DoesNotContain(names,static n=>n.StartsWith("Microsoft.Maui",StringComparison.Ordinal));
    }
}
```

## Web 版の扱い

決定: **後で検討(Functions 移行とセットで判断)**。

- ブラウザの fetch では User-Agent を任意に設定できない(Chromium 系)。MET Norway は利用規約でブラウザからの利用時は Origin ヘッダーによる識別を認めている(2026-10-04 確認)。NWS は User-Agent を必須としており、ブラウザからどう満たすかは**要確認**。
- CORS: 2026-10-04 時点で、気象庁 bosai(forecast / amedas / jmatile)、MET Norway Locationforecast、NWS のいずれも `Access-Control-Allow-Origin: *` を返すことを確認済み。CORS 自体は障害にならないが、仕様として保証されたものではない。
- 上記から Web 版は、NWS の User-Agent 要件の解決策が見つからない限り、中継サーバー(Functions)が前提になる。
- SkiaSharp は Blazor WebAssembly でも動作するため Core / Scene / Rendering は再利用できる。ただしダウンロードサイズが増える(規模は**要確認**)。
- 現時点の対応は、共有ライブラリを MAUI 非依存・AOT 互換に保つこと(不変条件 1・3)のみ。

## 要確認事項

- Azure Functions の .NET 10 ランタイム対応(移行時。パッケージは対応済み)
- ブラウザから NWS を利用する場合の User-Agent 要件(Web 版検討時)
- Blazor WebAssembly での SkiaSharp のダウンロードサイズ(Web 版検討時)
