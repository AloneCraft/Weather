# 静的調査 Geo+Core(読み取り専用エージェントの報告の要約。未検証の仮説。再現できたものだけ Bug 化)
- G-1 OutOfCoverage で DistanceKm=+Infinity → Remote の JSON 直列化(AllowNamedFloatingPointLiterals なし)で例外(Geo:LocationResolver.cs:47-54, Remote:RemoteJson.cs)。再現入力 GeoPoint(34.3,141.6), (43.8,146.75)
- G-2 国境付近で、国はポリゴン、表示名/行政区/TimeZone は他国の最寄り都市(例 GeoPoint(41.85,-6.3) → ES だが ブラガンサ/Europe/Lisbon)
- G-3 FindNearestPlace の探索範囲が緯度とともに東西に縮む → 高緯度で ResolveTimeZone が Etc/GMT± に落ちる(例 GeoPoint(65,-124.5))
- G-4 検索の正規化で「ヶ」と「ケ」が別文字(例 Search("茅ケ崎") が 茅ヶ崎市 に当たらない)
- G-5 Normalize の孤立サロゲートで例外の可能性 / 半角カナで日本語判定が外れる
- G-6 GridField.SampleNearest(NaN,NaN) が this[0,0] / 非循環格子の外周半セルが NaN
- G-7 GridField.Sample の循環継ぎ目で x==Columns になり誤った行/範囲外(Sample(-90,-1e-15))
- G-8 Observation/DailyForecast の値域検証なし(不変条件 6 の乖離。設計判断)
