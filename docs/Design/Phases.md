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
| 5. 地図トップ | トップを Windy 型の地図に作り替える(2026-10-04 のユーザーの要望) | 下記の試作が結論を出し、地図・吹き出し・予報シート・詳細がエミュレーターで動く |

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

## Phase 5: 地図トップ(2026-10-04)

ユーザーの要望: トップを文字中心の天気アプリではなく、Windy(windy.com)のような地図アプリにする。決定事項は Overview.md(方針 3 の追加・方針 5 の地図への適用)、Screens.md、Rendering.md、WeatherProviders.md「地図のデータ」。

| マイルストーン | 内容 |
|---|---|
| M1 試作 | P1 GRIB2 の復号、P2 気象庁のタイル、P3 性能、P4 位置合わせ(結果は Rendering.md) |
| M2 データ層 | Core の型、Grib2Decoder、GfsProvider、MapDataService、Range 対応の HTTP キャッシュ、JapanArea |
| M3 描画 | MapCamera(Web メルカトル)、基図、格子・タイル・等圧線・粒子・矢印 |
| M4 Presentation | MapViewModel、時間軸、吹き出し、予報シート、検索からの移動 |
| M5 App | 地図の画面・予報シート・詳細、描画ホストの一般化、旧トップと地図で選ぶ画面の削除 |
| M6 仕上げ | 設計書・README |

## 実装の状況(2026-10-04)

リポジトリ: https://github.com/AloneCraft/Weather(公開)。CI(GitHub Actions)で Windows のテスト、Android のビルド、macOS での iOS のビルド(ウィジェット拡張を含む)を PR・push ごとに実行する。

| フェーズ | 状況 | 確認方法 |
|---|---|---|
| Phase 0 基盤と試作 | 完了。ソリューション・規約・CPM・BannedApiAnalyzers・アーキテクチャテスト・CI・GeoDataBuilder | ビルド(警告 0)・テスト・CI |
| Phase 1 MVP | 完了。3 機関の予報、気象庁(r8)と NWS の警報、シーン描画、検索・お気に入り、出典表示、オフライン時の古いキャッシュ表示 | 単体テスト・実 API の契約テスト 11 件・Android エミュレーターでの動作確認 |
| Phase 2 履歴と地図 | 完了。観測履歴(同期・間引き・保持期間・グラフ)、世界地図での地点選択 | 単体テスト(SQLite)・エミュレーターで観測履歴(アメダス東京)を確認 |
| Phase 3 仕上げ | 完了(iOS の実機確認を除く)。英語 UI、読み上げ用の要約、警報の通知、ウィジェット(Android の AppWidget・iOS の WidgetKit 拡張)、背景更新(WorkManager・BGTaskScheduler)、MET の背景取得の二重防止 | 単体テスト。Android エミュレーターで、ウィジェット・画面なしでの背景起動・警報の通知(小笠原村の波浪警報)を確認。iOS は CI でビルドと拡張の同梱を確認 |
| Phase 4 発展 | Functions への移行を実装(中継 API・RemoteWeatherService)。Azure への配置はしていない。Web 版は未着手(後で検討) | 単体テスト・記録データでの結合テスト・Functions のビルド |
| Phase 5 地図トップ | 完了(iOS の実機確認を除く)。GFS 0.5° と気象庁の地図タイル・アメダス・海上分布予報、風の粒子・降水・気温・雲と気圧、時間軸と再生、吹き出し・予報シート・空のシーンの詳細 | 単体テスト(GRIB2・地図のデータ・描画の不変条件・ゴールデン画像・VM)、実 API の契約テスト(地図)、Android エミュレーターで各層・タップ・シート・詳細・再生を確認、風の層で約 58.5 fps(エミュレーター) |

試作 S1〜S5 の結果:

| # | 結果 |
|---|---|
| S1 iOS SKMetalView | ハンドラーを実装し、CI(macOS・Xcode 26.6)でシミュレーター向けにビルドできることを確認(警告 0)。実機での描画・フレームレートは未確認(Mac / iPhone が必要) |
| S2 Android SKGLView | エミュレーター(Android 16)で連続描画・画面遷移・背景移行を確認。中位機種の実機での 60 fps は未確認 |
| S3 GeoDataBuilder | 合計約 4.3 MB(目標 15 MB 以下)。解決・検索はテストとエミュレーター(ローマ字・かな検索)で確認 |
| S4 Microsoft.Data.Sqlite | Windows のテスト、Android エミュレーターで Debug と Release(トリミングあり。警告 0)の両方で動作を確認(お気に入りの保存・観測履歴・世界地図)。iOS リリースビルド(AOT)での動作は未確認 |
| S5 Adapter | 記録データと実 API の両方で確認 |

### エミュレーターでの確認で見つけて直した不具合

- 明るい空(曇り)でカード内の白い文字が読めない → カードの背景を常に濃い半透明にした。
- 出典が画面下部の暗い地形と同じ色で読めない → 出典をカードに載せた(方針 7)。
- お気に入りの変更後に全地点を作り直していた(読み込み中のスワイプでカルーセルが途中で止まる) → 既存の地点を使い回し、追加分だけ取得する。
- グラフの時刻軸で日付と時刻が重なる・左端の目盛りが切れる → 文字幅を測って判定する。
- WorkManager 2.11 が新しい AndroidX を引き込みクラスが重複 → MAUI と同じ AndroidX に依存する 2.10.3 に固定。

### 残作業と理由

- **iOS の実機確認**(SKMetalView の描画性能、BGTaskScheduler、通知、ウィジェット、SQLite のリリースビルド): iPhone と Mac(署名)が必要。
- **Android の中位機種の実機での性能確認**: エミュレーターは GPU 性能が実機と異なる(地図の風の層・空のシーン)。
- **地図の規約の確認**(公開前): 気象庁の地図タイルをアプリから取得する条件、GFS を日本以外で表示することの気象業務法上の扱い(日本周辺では表示しない)。
- **公開前の確定事項**: 署名鍵(Android は環境変数・シークレットで渡す仕組みと release ワークフローを用意済み。iOS は Mac で証明書とプロビジョニング)、iOS の App Group の登録(ウィジェットを同梱する場合)。アプリ ID は `io.github.alonecraft.soramoyou`(iOS の App Group は `group.io.github.alonecraft.soramoyou`)、プライバシーポリシーは docs/Store/PrivacyPolicy.md(公開者 AloneCraft、連絡先は GitHub Issues、2026-10-04 施行)、User-Agent の連絡先は公開リポジトリ `github.com/AloneCraft/Weather` に確定した。
- **Functions の配置**(任意): Azure のサブスクリプションが必要。Azure 側の .NET 10 ランタイム対応は配置時に**要確認**。公開時はレート制限(API Management 等)を前提にする。
- **Web 版**: 後で検討(SolutionStructure.md)。
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
