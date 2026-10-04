# 静的調査 Scene(読み取り専用エージェントの報告の要約。すべて未検証の仮説)
- 天体計算(太陽 NOAA 式・月)は式を照合し、不具合の候補なし。
- S-1 SceneTimeline のキーフレームが Later では副天気になる(中点 `>=`)。2 番目以降の区間の前半に主天気が出ない。公開 API では再現可能、現行アダプターでは到達性が低い(6h 以上の Later なし)。
- S-2 Blend が型 None でも連続値(Intermittency・Haze.Density・ThunderActivity)を補間して非 0 にする。
- S-3 日別経路が現地日付をまたぐ DayPart(NWS の Tonight)を拾えず Daily にフォールバックする。到達性は低い。
- S-4 DayPart と日別で WindText の扱いが異なる(仕様が曖昧)。
- S-5 (a) 不明な TimeZoneId を黙って UTC として扱う (b) DST 切替日に日別の Start/End が 1 時間ずれる。
- S-6 Condition に降水がなく PrecipitationMm>0 のとき降水を作らない(仕様が曖昧)。
- S-7 mm=0 と極小量で強度が非単調(仕様どおりの設計上の不連続)。
- S-8 副天気の降水の雲量下限が 0.6 固定(仕様は 0.8、Showery は 0.6)。
