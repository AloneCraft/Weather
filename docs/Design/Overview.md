# 設計概要

アニメーションやグラフィカルな表現で天気を見せる天気予報アプリ(.NET MAUI / iOS・Android)。

- トップは Windy 型の地図: 風の粒子・降水・気温・雲と気圧を地図の全面に描き、時間軸で動かす(2026-10-04 に変更)
- 地点をタップすると予報シート、引き上げると空のシーンを背景にした予報の詳細(時間別・日別)
- 観測履歴の表示
- 世界中の地域を選んで表示

## 決定済みの方針(変更しない)

1. クライアント: .NET MAUI(iOS / Android)
2. 構成: クライアントから各気象機関の API を直接取得する(自前サーバーなし)
3. データソース(キー不要のもの)
   - 日本: 気象庁 bosai 系 JSON(予報・アメダス・警報)と地図タイル(jmatile: 雨雲の動き・今後の雨・天気分布予報・海上分布予報)
   - 米国: NWS API (api.weather.gov)
   - その他の全地域: MET Norway Locationforecast
   - 地図の格子(日本周辺以外): NOAA GFS(AWS Open Data。パブリックドメイン)。**2026-10-04 にユーザーの決定で追加**(地図トップに面のデータが必要なため。地点の予報は上の 3 機関のまま)
4. 地点の国を判定し、上記の Provider に自動で振り分ける
5. 日本域は気象庁の発表をそのまま表示する(気象業務法の予報業務許可の問題を避けるため、他ソースのデータを日本域の予報として加工・表示しない)。地図でも日本周辺域には GFS を描かない(気象庁のタイル・アメダスだけ。気象庁の予報期間外は空白)
6. 各 API の利用規約を守る: User-Agent の明示、Expires / Last-Modified に従った端末内キャッシュ、座標の丸め
7. 全データに出典(機関名・発表時刻・ライセンス)を持たせ、UI に表示する
8. アニメーションは SkiaSharp による 2D 描画。予報データを「シーン状態」(空・太陽高度・雲量・降水種別と強度・風・雷・霧)に変換し、レイヤー描画する
9. 将来サーバーレス(Azure Functions)に移行できるよう、Provider は共通ライブラリに分離する

## 技術前提

- .NET 10(LTS)/ C# 14
- iOS ビルドは Mac(Pair to Mac / ローカル)で行う

## 設計書一覧

| # | 設計書 | 状態 |
|---|---|---|
| 1 | [SolutionStructure.md](SolutionStructure.md) ソリューション・プロジェクト構成 | 合意済み |
| 2 | [DataModel.md](DataModel.md) 共通データモデル | 合意済み |
| 3 | [WeatherProviders.md](WeatherProviders.md) IWeatherProvider と各 Adapter | 合意済み |
| 4 | [CacheAndRouting.md](CacheAndRouting.md) キャッシュ層・国判定・Provider 振り分け | 合意済み |
| 5 | [SceneState.md](SceneState.md) シーン状態と変換ロジック | 合意済み |
| 6 | [Rendering.md](Rendering.md) SkiaSharp 描画設計 | 合意済み |
| 7 | [Screens.md](Screens.md) 画面構成と画面遷移 | 合意済み |
| 8 | [History.md](History.md) 履歴機能 | 合意済み |
| 9 | [TestStrategy.md](TestStrategy.md) テスト方針 | 合意済み |
| 10 | [Phases.md](Phases.md) 開発フェーズ分け | 合意済み |

## 当初の未決事項の決定

| 未決事項 | 決定 | 設計書 |
|---|---|---|
| Web 版の扱い | 後で検討(Functions 移行とセットで判断) | SolutionStructure.md |
| 地点検索・逆ジオコーディングのデータソース | すべてオフライン。日本 = 気象庁 area.json + 市町村ポリゴン、世界 = GeoNames cities5000 + 日本語別名、国境 = Natural Earth(日本の視点版) | CacheAndRouting.md |
| 端末内の履歴保存の方式 / 保持期間 | SQLite(Microsoft.Data.Sqlite)。日本・米国の観測のみ、既定 1 年 | CacheAndRouting.md / History.md |
| グラフライブラリの選定 | SkiaSharp 4 で自前描画 | Rendering.md |
| 世界地図・地点選択画面の実装方法 | Natural Earth のオフラインベクターを SkiaSharp 4 で自前描画(Phase 2)。2026-10-04 にトップの地図(Web メルカトル)へ置き換え | Rendering.md / Screens.md |
| 地図トップのデータ・表現(2026-10-04) | 日本周辺 = 気象庁の地図タイル・アメダス、それ以外 = NOAA GFS 0.5°。層は風(粒子)・降水・気温・雲と気圧。日本周辺の風は現在時刻のアメダスの矢印と海上分布予報の風向。背景は自前のベクター地図 | Rendering.md / WeatherProviders.md / Screens.md |

## 主な追加ガードレール(設計の過程で判明)

- 日本周辺域(海上・北方領土等を含む)は気象庁のみ。区域外は 20 km 以内なら最寄り区域、それ以外は「対象外」(CacheAndRouting.md)
- 気象業務法第 23 条: 日本域で警報相当の情報を独自に作らない(WeatherProviders.md)
- 気象庁の出典表示「出典:気象庁ホームページ」と加工の明示(WeatherProviders.md)
- MET Norway はアプリ未使用時に取得しない(バックグラウンド・ウィジェット不可)(WeatherProviders.md)
- 地図: 日本周辺域(地点の解決と同じ範囲)に GFS の色・粒子・等圧線・吹き出しの値を出さない。格子の NaN・シェーダーのマスク・吹き出しの判定で三重に守る(Rendering.md)

## 実装の状況

Phase 0〜4 と地図トップへの作り替え(Phase 5)を実装済み。詳細と残作業は [Phases.md](Phases.md) の「実装の状況」を参照。
