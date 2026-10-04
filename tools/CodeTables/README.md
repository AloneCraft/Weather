# 気象庁のコード表の再生成

気象庁 bosai の天気コード・警報コードは公開仕様ではなく、気象庁サイトの JS に定義されている。変更を検知したら(実 API の契約テストで未知のコードが出たら)次の手順で更新する。

1. 気象庁の予報ページ(`https://www.jma.go.jp/bosai/forecast/`)の HTML を取得し、`\uXXXX` をデコードして `R.TELOPS={...}` を抜き出し、`telops.json` に保存する(キーはコード、値は `[昼画像, 夜画像, 区分, 名称, 英語名]`)。
2. 気象庁の警報ページ(`https://www.jma.go.jp/bosai/warning/`)の JS から `コード:{shortNameParts:…,elem:"…",level:NN}` を抜き出し、`jma_warning_codes.json` に `{コード: {elem, level}}` として保存する。
3. `python tools/CodeTables/generate.py` を実行する。
4. 生成された `src/Weather.Providers/Jma/*.Table.cs` の差分をレビューし(変換規則は WeatherProviders.md 付録 A)、`tests/Weather.Providers.Tests` を通す。テストの記録データ `Fixtures/<日付>/jma/telops.json` も更新する。

新しい基本語(例: 新しい天気の語)が増えた場合は、`generate.py` の `ATOMS` と WeatherProviders.md 付録 A の規則を同時に更新する。
