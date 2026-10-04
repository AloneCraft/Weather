# 空模様(Weather)

アニメーションとグラフィカルな表現で天気を見せる .NET MAUI(iOS / Android)の天気予報アプリ。

- 日本: 気象庁(予報・警報・アメダス)、米国: NWS、その他: MET Norway を、地点の国で自動的に振り分けて直接取得する(自前サーバーなし)
- 日本域は気象庁の発表をそのまま表示する(他ソースのデータを日本域の予報として表示しない)
- すべてのデータに出典(機関名・発表時刻・ライセンス)を表示する
- 予報を「シーン状態」に変換し、SkiaSharp 4(SkSL シェーダー・パーティクル)で空を描く

設計書は [docs/Design/Overview.md](docs/Design/Overview.md) から辿れる。

## 構成

| プロジェクト | 役割 |
|---|---|
| `src/Weather.Core` | データモデル・抽象(MAUI 非依存) |
| `src/Weather.Providers` | 気象庁・NWS・MET の Adapter、HTTP キャッシュ、振り分け、IWeatherService |
| `src/Weather.Geo` | オフラインの地点解決・検索(気象庁の区域、GeoNames、Natural Earth) |
| `src/Weather.Scene` | 予報 → シーン状態の変換、天体計算 |
| `src/Weather.Rendering` | SkiaSharp 4 によるシーン・グラフ・世界地図の描画 |
| `src/Weather.Infrastructure` | ファイルの HTTP キャッシュ、SQLite(お気に入り・観測履歴) |
| `src/Weather.Presentation` | ViewModel(CommunityToolkit.Mvvm) |
| `src/Weather.App` | MAUI アプリ(Android は SKGLView、iOS は SKMetalView の自作ハンドラー)。通知・ウィジェット・背景更新の各 OS の実装 |
| `src/Weather.Widget.iOS` | iOS のウィジェット(WidgetKit 拡張。Swift / XcodeGen) |
| `src/Weather.Remote` | Phase 4: 中継の電文・サーバー側の要求処理・RemoteWeatherService(Core のみに依存) |
| `src/Weather.Functions` | Phase 4: Azure Functions(isolated worker)の中継 API。既定のアプリは使わない(直接取得) |
| `tools/Weather.SceneLab` | 描画調整ツール(WPF、記録データの予報を時間軸で再生) |
| `tools/Weather.GeoDataBuilder` | 地理データの生成 |

## ビルドとテスト

.NET 10 SDK(`global.json` で 10.0.401 以降に固定)と MAUI ワークロード(android / ios)が必要。

```bash
dotnet test --solution Weather.slnx
```

```bash
dotnet build src/Weather.App/Weather.App.csproj -f net10.0-android
```

- iOS は Windows 上では C# のコンパイルまで。パッケージ作成・署名・実機実行には Mac(Pair to Mac / ローカル)が必要。CI(macOS)ではシミュレーター向けにビルドしている。
- iOS のウィジェットを同梱する場合は、Mac で拡張をビルドしてから App をビルドする(App Group `group.io.github.alonecraft.soramoyou` を有効にしたプロビジョニングが必要):

```bash
cd src/Weather.Widget.iOS && xcodegen generate && xcodebuild -project WeatherWidget.xcodeproj -target WeatherWidget -configuration Release -sdk iphonesimulator SYMROOT=build CODE_SIGNING_ALLOWED=NO build
```

```bash
dotnet build src/Weather.App/Weather.App.csproj -f net10.0-ios -p:IncludeWidgetExtension=true
```

- Functions(任意)をローカルで動かすには Azure Functions Core Tools が必要。`src/Weather.Functions/local.settings.example.json` を `local.settings.json` に複製してから `func start` を実行する。アプリを中継に切り替えるには、MauiProgram で `AddWeatherProviders` の後に `AddRemoteWeatherService(new Uri("https://<関数アプリ>/api/"),userAgent)` を呼ぶ。
- リリース用の Android AAB は GitHub Actions の release ワークフロー(手動実行)で作る。署名鍵はリポジトリに置かず、シークレット(ANDROID_KEYSTORE_BASE64 / ANDROID_KEY_ALIAS / ANDROID_STORE_PASSWORD / ANDROID_KEY_PASSWORD)か環境変数(WEATHER_ANDROID_*。Weather.App.csproj)で渡す。未設定ならデバッグ鍵で署名される。
- Android エミュレーターでの確認手順は [docs/Design/TestStrategy.md](docs/Design/TestStrategy.md)「エミュレーターでの確認手順」。
- 実 API の契約テストは通常の実行では動かない。週 1 回(CI の schedule)または手動で実行する。

```bash
dotnet test --project tests/Weather.Providers.LiveTests -- --explicit only
```

- ゴールデン画像(描画の代表 6 シーン)は見た目を調整したら再生成し、差分を目視で確認してからコミットする。

```bash
WEATHER_UPDATE_GOLDEN=1 dotnet test --project tests/Weather.Rendering.Tests
```

## データの再生成

- 地理データ(`src/Weather.Geo/Data/*.bin`): `dotnet run --project tools/Weather.GeoDataBuilder`(元データは `.cache` に保存し再利用する)
- 気象庁の天気コード表・警報コード表(`src/Weather.Providers/Jma/*.Table.cs`): [tools/CodeTables/README.md](tools/CodeTables/README.md)
- 記録データ(`tests/Weather.Providers.Tests/Fixtures/`): 取得日ごとのフォルダに保存する

## 公開前に確定すること

- 署名(アプリ ID は `io.github.alonecraft.soramoyou` に確定。変える場合は iOS の App Group・BGTask の識別子・ウィジェット拡張の Bundle ID も合わせる)
- 公開時に各 API の利用規約を再確認する(WeatherProviders.md「利用規約・法令」)
- ストア用のプライバシーポリシー: [docs/Store/PrivacyPolicy.md](docs/Store/PrivacyPolicy.md)(ストアに登録する URL は https://github.com/AloneCraft/Weather/blob/main/docs/Store/PrivacyPolicy.md )

## ライセンス表示

気象庁(公共データ利用規約 第 1.0 版、出典:気象庁ホームページ)、National Weather Service(米国政府のオープンデータ)、MET Norway(CC BY 4.0 / NLOD 2.0)、GeoNames(CC BY 4.0)、Natural Earth(パブリックドメイン)。アプリ内の「データと出典」にも表示する。
