# 開発フェーズ分け

## 決定事項

| 論点 | 決定 |
|---|---|
| MVP の範囲 | 3 機関の予報、気象庁と NWS の警報、シーン描画、地点の検索とお気に入り、出典の表示。観測履歴と世界地図は Phase 2 |
| 対象 OS | MVP から Android と iOS を並行して開発する(Mac あり) |

## 全体像

| フェーズ | 内容 | 終了条件 |
|---|---|---|
| 0. 基盤と試作 | ソリューション、規約、CI、技術的リスクの試作 | 下記の試作がすべて結論を出している |
| 1. MVP | 予報・警報・シーン・検索・お気に入り・出典 | 下記の受け入れ基準を満たし、ストア審査に出せる |
| 2. 履歴と地図 | 観測履歴、世界地図、描画の拡充 | History.md と地図の機能が受け入れ基準を満たす |
| 3. 仕上げ | 英語化、アクセシビリティ、性能、ウィジェット等 | リリース品質 |
| 4. 発展(任意) | Functions 移行、Web 版の検討 | 個別に判断 |

## Phase 0: 基盤と試作

### 基盤
- `Weather.slnx`、各プロジェクト(SolutionStructure.md)、standard-solution-rules の適用(.editorconfig / AI_SYNTAX_RULES.md / AGENTS.md / CLAUDE.md)。
- Directory.Build.props(Nullable、解析レベル、ライブラリの IsAotCompatible)、Directory.Packages.props(SkiaSharp 4.153.x 等を固定)。
- BannedApiAnalyzers(`DateTime.Now` / `UtcNow` の禁止)、アーキテクチャテスト。
- git リポジトリの作成と GitHub での公開(User-Agent の連絡先に使う)、CI(TestStrategy.md)。

### 試作(リスクの高い順)

| # | 試作 | 確かめること | 失敗した場合の代替 |
|---|---|---|---|
| S1 | iOS の SKMetalView 自作ハンドラー+雲シェーダー | 連続描画・60 fps・背景移行時の停止・ProMotion | MAUI 標準の SKGLView(OpenGL ES) |
| S2 | Android の SKGLView 連続描画+パーティクル 3,000 個 | 中位機種で 60 fps、フレームごとのメモリ確保なし | 品質段階の上限を下げる |
| S3 | GeoDataBuilder | 地理データの合計 15 MB 以下、検索・解決の速度(端末で 50 ms 以内) | cities15000 に縮小、ポリゴンをさらに簡略化 |
| S4 | Microsoft.Data.Sqlite の iOS リリースビルド(トリミング・AOT) | 警告と実機動作 | 警告の個別対応、または保存形式の見直し |
| S5 | 各 Adapter の最小実装を記録データで動かす | 気象庁の日別合成、MET の区間選択、NWS の昼夜の組み合わせ | — |

## Phase 1: MVP

| マイルストーン | 内容 | 主な設計書 |
|---|---|---|
| M1 データ層 | Core モデルと不変条件、3 Adapter、コード表、Mapper テスト | DataModel / WeatherProviders |
| M2 キャッシュと振り分け | HttpCacheHandler、FileHttpCacheStore、Geo(解決・検索)、Router、WeatherService | CacheAndRouting |
| M3 シーン | SceneConverter、天体計算、SceneRenderer の基本レイヤー(空・太陽・月・星・雲・雨・雪・霧・稲光)、品質段階、SceneLab | SceneState / Rendering |
| M4 画面 | メイン、時間別、日別、警報、検索、地点の管理、設定、データと出典。お気に入り(SQLite の favorites テーブル) | Screens |
| M5 仕上げ | 出典表示の確認、オフライン時の動作、権限、ストア用の資料(プライバシーポリシー、ライセンス一覧) | 全体 |

### MVP の受け入れ基準
- 東京・那覇・ニューヨーク・ホノルル・ロンドン・シドニーで予報が表示され、各カードに正しい出典が出る。
- 日本周辺の対象外地点で「対象外」と表示され、MET / NWS のデータが一切表示されない。
- 気象庁の警報(新しい r8 形式)と NWS の警報が表示される。警報非対応の地域では「提供されていない」と表示される。
- 機内モードで、前回のデータが取得時刻付きで表示される。
- シーンが中位機種(Android)・iPhone で 60 fps(省電力時は 30 fps)で動き、背景では停止する。
- 実 API の契約テストが通る。PR ごとのテストがすべて通り、警告がゼロ。

## Phase 2: 履歴と地図

- 観測履歴(History.md): 観測所の選択、同期、間引き、保持期間、グラフ。SQLite のマイグレーションで観測テーブルを追加する。
- 世界地図による地点選択(Rendering.md の Map)。
- 描画の拡充: 跳ね返り、雲の陰影、季節の演出。

## Phase 3: 仕上げ

- 英語 UI。
- アクセシビリティの確認(VoiceOver / TalkBack、コントラスト、視差効果)。
- 性能の調整(低位機種)。
- ウィジェット: 気象庁・NWS のデータのみ(MET はバックグラウンド取得が規約で禁止のため対象外)。iOS の BGTaskScheduler は実行が保証されないことを前提に設計する。
- 警報の通知: 同上の制約に加え、気象業務法第 23 条に沿って気象庁の警報をそのまま伝える形に限る。

## Phase 4: 発展(任意)

- Azure Functions への移行: Providers / Geo / キャッシュをサーバー側で動かし、クライアントは RemoteWeatherService に切り替える。移行時に isolated worker の .NET 対応版を確認する。
- Web 版(Blazor): Functions を前提に検討する(SolutionStructure.md)。

## 実装の状況(2026-10-04)

| フェーズ | 状況 | 確認方法 |
|---|---|---|
| Phase 0 基盤と試作 | 完了。ソリューション・規約・CPM・BannedApiAnalyzers・アーキテクチャテスト・CI 定義・GeoDataBuilder | ビルド(警告 0)・テスト |
| Phase 1 MVP | 実装完了。3 機関の予報、気象庁(r8)と NWS の警報、シーン描画、検索・お気に入り、出典表示、オフライン時の古いキャッシュ表示 | 単体テスト 78 件・実 API の契約テスト 11 件(東京・札幌・ニューヨーク・ホノルル・ロンドン・シドニーの予報、東京・稚内・ニューヨークの警報、アメダスと NWS の観測)。Android / iOS のビルド |
| Phase 2 履歴と地図 | 実装完了。観測履歴(同期・間引き・保持期間・グラフ)、世界地図での地点選択 | 単体テスト(SQLite)・ビルド |
| Phase 3 仕上げ | 一部。英語 UI(resx、日本語既定)、読み上げ用の天気の要約、稲光の点滅制限・視差効果の設定、品質段階の自動調整 | 単体テスト(言語・要約・稲光の頻度) |
| Phase 4 発展 | 未着手(任意)。Functions のワーカーパッケージは net10.0 に対応済み(Microsoft.Azure.Functions.Worker 2.52.0) | — |

試作 S1〜S5 の結果:

| # | 結果 |
|---|---|
| S1 iOS SKMetalView | ハンドラーを実装し iOS 向けにコンパイルできることを確認。実機での描画・フレームレートは未確認(Mac が必要) |
| S2 Android SKGLView | `HasRenderLoop` による連続描画を実装しビルドを確認。実機・エミュレーターでの性能は未確認 |
| S3 GeoDataBuilder | 合計約 4.3 MB(目標 15 MB 以下)。解決・検索はテストで確認 |
| S4 Microsoft.Data.Sqlite | Windows でのテストは成功。iOS リリースビルド(トリミング・AOT)での動作は未確認 |
| S5 Adapter | 記録データと実 API の両方で確認 |

### 残作業と理由

- **実機・エミュレーターでの確認**(描画性能、背景移行時の停止、権限、オフライン、VoiceOver / TalkBack): この環境にはエミュレーターも実機もなく、iOS は Mac が必要。
- **ウィジェット・警報の通知**: バックグラウンド取得(Android WorkManager / iOS BGTaskScheduler)と実機での検証が必要。MET はバックグラウンド取得が規約で禁止のため、気象庁・NWS のみが対象。通知の文面は気象業務法第 23 条に沿い、気象庁の警報をそのまま伝える形に限る。
- **公開前の確定事項**: アプリ ID と署名、ストア用のプライバシーポリシー。User-Agent の連絡先は公開リポジトリ `github.com/AloneCraft/Weather` に確定した。
- **要確認のまま残る仕様**: 気象庁の時系列予報の天気区分と風速階級、警報の電文種別と状態文字列、氾濫注意報のコード、アメダスの品質フラグと elems、NWS の qualityControl と雲量区分、MET の altitude、VI / AS / MP の NWS 対応。実 API の契約テスト(週 1 回)で変化を検知する。

## リスクと対策

| リスク | 影響 | 対策 |
|---|---|---|
| 気象庁 bosai JSON の予告なしの変更(2026-05-28 の警報形式の変更が実例) | 日本の表示が止まる | URL・コード表をデータとして一元管理、週 1 回の実 API 契約テスト、変更時は記録データを更新 |
| NWS の API キー移行 | 米国の取得が止まる | 告知を監視。キー導入時は方針 3(キー不要)の例外として扱うか、Functions 経由にするかを判断 |
| iOS の Metal ホストの不具合 | iOS の描画性能 | 試作 S1 で早期に判断。代替は SKGLView |
| SkiaSharp 4 の API 変化(SkSL・書体) | 描画コードの修正 | Rendering に閉じ込め、ゴールデン画像で検知 |
| iOS のトリミング・AOT による実機のみの不具合 | リリース版の不具合 | IsAotCompatible、リフレクションを使わない JSON、CI での iOS リリースビルド |
| 低位 Android 機種の性能 | カクつき・発熱 | 品質段階の自動調整、省電力時は 30 fps |
| 法令・規約の解釈(方針 5、気象業務法第 23 条) | 公開の可否 | 日本周辺域は気象庁のみ・対象外の扱いをテストで保証。公開前に規約を再確認 |
