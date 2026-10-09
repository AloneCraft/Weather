# 静的調査 Providers 変換+キャッシュの要約(未検証の仮説)
- P-1(高) JmaAlertMapper: 発表中の警報がない地点にも、別区域の電文の headlineText が AlertSet.Headline に積まれる(matched が kinds の code を見ない。JmaAlertMapper.cs:36,41,64-66)
- P-2(中) JmaForecastMapper: 週間予報の区域が一致しないと areas[0] に黙って置換(伊豆諸島北部/南部で東京地方の値。113,132,84 行)
- P-3(中・安全情報) 未知の警報コードを表示から丸ごと落とす(JmaAlertMapper.cs:47-51)。ガードレール 6 は原文表示を求める
- P-4(中) HttpCacheHandler: 本文読み込み(88 行)がタイムアウト/stale 切替の try の外
- P-5(中) NwsMapper: 先頭が Overnight だとその日の Tonight が捨てられる(仮説部分あり)
- P-6(中〜低) キャッシュの LastAccessedAt がヒットで更新されない(= I-4)
- P-7(中〜低) NWS 観測の URL に秒単位の end が入りキャッシュが効かない
- P-8(低) JMA 時系列の重複/逆順で Forecast コンストラクタの ArgumentException が漏れ予報全体が失敗
- 見送りメモ: MET 区間選択、304 の Expires、JmaDefinitions の更新頻度、負のゼロ
