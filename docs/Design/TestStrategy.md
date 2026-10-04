# テスト方針

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| テストフレームワーク | xUnit v3(`xunit.v3` パッケージ)+ Microsoft Testing Platform | AI_SYNTAX_RULES.md のテスト例が xUnit 形式(`[Fact,Trait(...)]`) |
| アサーション | xUnit 標準の Assert | FluentAssertions は v8 から商用ライセンス |
| 時刻 | `Microsoft.Extensions.TimeProvider.Testing` の FakeTimeProvider | 本体は `TimeProvider` 注入が前提(SolutionStructure.md の不変条件 4) |
| HTTP | 自作の `FixtureHttpMessageHandler`(URL → 記録ファイル、送信内容を記録) | モックライブラリ不要。User-Agent・座標桁数・条件付きヘッダーも検証できる |
| モック | 手書きの Fake | 外部から観測できる振る舞いを検証し、実装の写しになるのを避ける |

## テスト規約(AI_SYNTAX_RULES.md より)

- テストクラス名 = テスト対象のクラス名。テストメソッドの順序は、対象の public メンバーの順序に合わせる。
- 1 メソッドの複数ケースは、同じテストメソッド内で `{}` スコープに分け、コメントでケースを明示する。
- `#if` でバージョン差分を分岐しない。

```csharp
public class WeatherProviderRouter{
    [Fact,Trait("Category","Unit")]public void RouteForecast(){
        {
            //東京(区域内)は気象庁
        }
        {
            //北方領土(日本周辺域・対象外)はどの Provider にも振り分けない
        }
        {
            //バンクーバーは MET Norway
        }
    }
}
```

## テストの層

| 層 | プロジェクト | 内容 | 実行 |
|---|---|---|---|
| アーキテクチャ | Core.Tests | App 以外が Microsoft.Maui.* を参照しない、Providers が Geo を参照しない | PR ごと |
| モデルの不変条件 | Core.Tests | DataModel.md の不変条件 1〜6(区間の順序、範囲、日本域の気象庁限定) | PR ごと |
| Mapper(記録データ) | Providers.Tests | 記録した実レスポンス → model。気象庁の日別合成(05/11/17 時発表のそれぞれ)、MET の区間選択(1h → 6h の切り替わり、末尾の instant のみ)、NWS の昼夜の組み合わせ | PR ごと |
| コード表の網羅 | Providers.Tests | 気象庁の天気コード 118 件・警報コード 39 件、MET シンボル 41 件(誤綴り含む)、NWS weather enum 23 件がすべて変換される。未知のコードは null + 診断ログ | PR ごと |
| Adapter(HTTP 込み) | Providers.Tests | FixtureHttpMessageHandler で全エンドポイントを再生。User-Agent が付く、座標が小数 2 桁以下、主プロダクト失敗で例外、補助プロダクト失敗で Issues | PR ごと |
| キャッシュ | Providers.Tests | FakeTimeProvider で、鮮度(max-age・Expires・Age)、条件付き GET と 304、MET の If-Modified-Since の完全一致、通信失敗時の古いキャッシュと IsStale、警報の許容期間(6 時間)、同時リクエストの集約 | PR ごと |
| 地点の解決・振り分け | Geo.Tests / Providers.Tests | 表形式: 東京・那覇・札幌の各区、小笠原、与那国、東京湾の海上(Nearest)、沖合 100 km・北方領土(対象外)、竹島・尖閣(気象庁の区域定義に従う)、グアム・プエルトリコ・ホノルル(NWS)、バンクーバー、太平洋上(XX → MET)、国境付近。日本周辺域で MET / NWS を返さない | PR ごと |
| 地点検索 | Geo.Tests | 日本語・かな・ローマ字・英語、表記ゆれ(NFKC)、並び順 | PR ごと |
| シーン変換 | Scene.Tests | SceneState.md の不変条件 1〜8。表形式(Condition → 期待するシーン)と、性質ベースのテスト(CsCheck: 降水なし → 強度 0、降水量に対する単調性) | PR ごと |
| 天体計算 | Scene.Tests | NOAA の基準値との比較(高度 ±0.5°)。基準値の収集は**要確認** | PR ごと |
| 描画 | Rendering.Tests | 全レイヤー × 全品質段階のオフスクリーン描画スモーク、代表シーンのゴールデン画像(許容誤差付き)、シェーダー失敗時の代替描画、定常状態の 1 フレームあたりの割り当て 4 KB 未満 | PR ごと(ゴールデン画像は Windows で生成・比較) |
| ViewModel | Presentation.Tests | Fake サービスで、状態の遷移(読み込み中 → 表示 / 古い / 対象外 / エラー)、ナビゲーション引数、キャンセル | PR ごと |
| 履歴(Phase 2) | Infrastructure.Tests | 一時ファイルの SQLite で、同期の冪等性、間引き、保持期間の削除、マイグレーション | PR ごと |
| 実 API の契約 | Providers.LiveTests(`[Fact(Explicit=true)]`。`-- --explicit only` で実行) | 実 API を叩き、DTO として読めること、コード表に未知のコードがないこと、エンドポイントが生きていること。失敗したら新しい記録データを取り、差分を調査する | 手動・定期(週 1 回、PR では実行しない) |
| 実機 | 手動チェックリスト | Android エミュレーター・実機、iPhone 実機: 描画のフレームレート、背景移行時の停止、権限、オフライン、ダークモード | リリース前 |

Geo.Tests・Infrastructure.Tests・Providers.LiveTests は SolutionStructure.md のテストプロジェクト一覧に含めている。

## 記録データ(フィクスチャ)

- 置き場所: `tests/Weather.Providers.Tests/Fixtures/{provider}/{yyyy-MM-dd}/{name}.json`。
- 2026-10-04 に設計調査で取得した実レスポンス(気象庁の府県天気予報・時系列予報・警報 r8・アメダス、MET complete / compact、NWS points / forecast / hourly / grid / alerts / observations)を最初の記録データにする。
- 特殊な時期(雪、台風、特別警報、極夜・白夜)の記録データは、発生したときに Live テストの仕組みで追加する。
- 記録データには取得日時と URL を README に残す。

## CI(GitHub Actions)

| ジョブ | ランナー | 内容 |
|---|---|---|
| test | windows-latest | net10.0 の全テスト(Live を除く)、ゴールデン画像の比較 |
| build-android | ubuntu-latest または windows-latest | App の Android ビルド(警告をエラーとして扱う) |
| build-ios | macos-latest | App の iOS ビルド(署名なし) |
| live(定期) | ubuntu-latest | Live テスト。失敗したら Issue を作る |

## 不変条件をテストへ移す対応表

| 不変条件 | 設計書 | テスト |
|---|---|---|
| 日本域は気象庁のみ | Overview 方針 5 / DataModel / CacheAndRouting | Forecast コンストラクタ、Router、地点解決 |
| 全データに出典 | Overview 方針 7 / DataModel | コンストラクタ引数(コンパイル時)+ Mapper テスト |
| User-Agent 必須・座標 2 桁 | WeatherProviders / CacheAndRouting | Adapter テスト(送信内容の検証) |
| MET の Expires・If-Modified-Since | WeatherProviders / CacheAndRouting | キャッシュテスト |
| MET をバックグラウンドで取得しない | WeatherProviders | 背景状態での取得要求が拒否されることを ViewModel / サービスのテストで検証 |
| MAUI 非依存 | SolutionStructure | アーキテクチャテスト |
| 降水確率で降水を作らない | SceneState | シーン変換テスト |
| 稲光の点滅 3 回 / 秒以下 | Rendering | 稲光レイヤーの決定的シミュレーションのテスト |

## テストを増やしすぎない

- コード表は、表そのものを 1 件ずつ検証するのではなく、網羅性(全コードが変換される)と不変条件(Precipitation と Intensity の整合など)を検証する。
- ゴールデン画像は代表 6 シーンに限る。見た目の調整のたびに更新が必要になるため、更新手順(画像を再生成し差分を目視で確認)を README に書く。
