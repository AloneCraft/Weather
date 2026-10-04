# 履歴機能(観測履歴)

Phase 2 で提供する(Phases.md)。

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| 対象 | 日本(アメダス)と米国(NWS)の観測のみ。他の地域は「この地域の観測データは提供されていません」と明示する | キー不要の観測ソースが日本・米国にしかない(MET Norway の観測 API Frost はキーが必要) |
| 端末での保持 | 既定 1 年。設定で 30 日 / 1 年 / 3 年 | 長期の推移を見る用途。容量は小さい(後述) |
| 取得方法 | 画面を開いたとき・アプリが前面に来たときに、サーバーに残っている範囲(気象庁 約 10 日、NWS 約 7 日)から欠けた分を補う | バックグラウンド取得はしない。週に 1 回程度アプリを開けば欠損なく蓄積できる |

## 観測事実(2026-10-04)

- アメダスの地点別ファイル `point/{station}/{yyyyMMdd}_{HH}.json`: 2026-09-24 分は取得できたが、09-22 分は 404。サーバーの保持期間は約 10 日。
- NWS `/stations/{id}/observations?start=&end=`: 6 日前は 313 件取得できたが、20 日前は 0 件。保持期間は約 7 日(正確な日数は**要確認**)。
- アメダスの地点別ファイルは 10 分ごとの値で、`maxTemp` / `minTemp`(と時刻)など、気象庁が計算した日最高・最低を含む。

## 観測所の選択

- 地点を登録したとき、最寄りの観測所(気温を観測している観測所、50 km 以内)を自動で選ぶ。利用者は変更できる。
- 選んだ観測所は、お気に入りごとに保存する。
- 気象庁: `amedastable.json` の `elems` で気温観測の有無を判定する(ビットの意味は**要確認**)。NWS: points の observationStations の先頭から選ぶ(距離順かどうかは**要確認**)。

## 同期の手順

1. `sync_state` から、その観測所の最新の観測時刻を得る。
2. 取得範囲 = max(最新の観測時刻, 現在 − サーバー保持期間) 〜 現在。
3. 気象庁: 範囲内の 3 時間ブロックのファイルを古い順に 1 本ずつ取得する(初回は最大約 80 本 × 約 9 KB)。HTTP キャッシュ層を通すため、取得済みのブロックは再取得しない。NWS: start / end を指定して 1 回で取得する(ページングの有無は**要確認**)。
4. 主キーが重複する行は無視して挿入する(何度実行しても同じ結果になる)。
5. サーバー保持期間より前に一度も取得できなかった区間は、欠測として扱う。補間しない。

同期は画面の表示をブロックしない。取得済みの範囲を先に表示し、追加分を後から反映する。

## 保存方式(SQLite)

`FileSystem.AppDataDirectory/weather.db`。Microsoft.Data.Sqlite と手書きの SQL。スキーマの版は `PRAGMA user_version` で管理し、前進のみのマイグレーションを起動時に適用する。

```sql
CREATE TABLE stations(
    station_key   TEXT PRIMARY KEY,     -- 'jma:44132' / 'nws:KDCA'
    provider      INTEGER NOT NULL,
    name          TEXT NOT NULL,
    latitude      REAL NOT NULL,
    longitude     REAL NOT NULL,
    elevation_m   REAL
);

CREATE TABLE observations(
    station_key       TEXT NOT NULL REFERENCES stations(station_key),
    observed_at_utc   INTEGER NOT NULL,     -- Unix 秒
    temperature_c     REAL,
    humidity_pct      REAL,
    precip_10m_mm     REAL,
    precip_1h_mm      REAL,
    precip_24h_mm     REAL,
    wind_speed_ms     REAL,
    wind_dir_deg      REAL,
    wind_gust_ms      REAL,
    pressure_hpa      REAL,
    sea_level_hpa     REAL,
    sunshine_1h_h     REAL,
    snow_depth_cm     REAL,
    visibility_m      REAL,
    suspect_mask      INTEGER NOT NULL DEFAULT 0,   -- 要素ごとの Suspect フラグ(ビット)
    weather_text      TEXT,
    PRIMARY KEY(station_key,observed_at_utc)
) WITHOUT ROWID;

CREATE TABLE daily_summaries(
    station_key   TEXT NOT NULL REFERENCES stations(station_key),
    local_date    TEXT NOT NULL,            -- yyyy-MM-dd(観測所の現地日付)
    max_temp_c    REAL,
    max_temp_at   INTEGER,
    min_temp_c    REAL,
    min_temp_at   INTEGER,
    precip_mm     REAL,
    is_derived    INTEGER NOT NULL,         -- 0 = 機関の値(気象庁)、1 = アプリで集計(NWS)
    PRIMARY KEY(station_key,local_date)
) WITHOUT ROWID;

CREATE TABLE sync_state(
    station_key       TEXT PRIMARY KEY REFERENCES stations(station_key),
    last_observed_utc INTEGER,
    last_synced_utc   INTEGER
);

CREATE TABLE favorites(
    place_id      TEXT PRIMARY KEY,
    display_name  TEXT NOT NULL,
    latitude      REAL NOT NULL,
    longitude     REAL NOT NULL,
    sort_order    INTEGER NOT NULL,
    station_key   TEXT REFERENCES stations(station_key)
);
```

## 間引きと集計

- **気象庁(10 分値)**: 直近 30 日は 10 分値を保持する。それより古い分は正時(:00)の行だけを残す。正時の行は気象庁自身の 1 時間降水量(`precipitation1h`)などを含むため、値を計算し直さずに済む(加工しない)。
- **日最高・最低**: 気象庁は地点別ファイルの `maxTemp` / `minTemp` を使う(機関の値、`is_derived = 0`)。NWS は保存した観測から計算する(`is_derived = 1`、UI に「アプリで集計」と表示)。
- **NWS**: 直近 30 日は全件(定時観測と特別観測)を保持する。それより古い分は各時間帯の最後の観測(定時観測は毎時 52 分前後)だけを残す。

## 容量の見積もり

気象庁の 1 観測所・1 年分は、10 分値 30 日(約 4,300 行)+ 正時値 335 日(約 8,000 行)で約 12,000 行。1 行約 100 バイトとして約 1.2 MB / 年。お気に入りが 10 地点でも約 12 MB で、許容範囲。

## 保持期間の管理

起動時と、設定で保持期間を変えたときに、期間外の行を削除する。削除後は必要に応じて VACUUM する(容量が大きく減った場合のみ)。

## 表示

- 期間: 24 時間 / 7 日 / 30 日 / 1 年。
- グラフ: 気温の折れ線(日最高・最低の帯)、降水量の棒、風(矢印)、日照時間。
- 欠測区間は線を途切れさせる(補間しない)。Suspect の値には気象庁サイトと同様に「)」の印を付ける。
- 出典: 「出典:気象庁ホームページ(アメダス)」/「National Weather Service」。

## プライバシーとバックアップ

- 履歴は端末内だけに保存し、外部に送らない。
- 設定に「履歴をすべて削除」を用意する。
- AppDataDirectory は OS のバックアップ(iCloud / Android の自動バックアップ)の対象になる。HTTP キャッシュ(CacheDirectory)は対象外にする。

## 不変条件(テストで検証)

1. 同期を何度実行しても、同じ行集合になる
2. 欠測区間を補間した値を保存しない
3. 間引き後に残る行は、元の行の部分集合(値を変えない)
4. 保持期間外の行が残らない
5. 日本・米国以外の地点では観測の取得を試みない
