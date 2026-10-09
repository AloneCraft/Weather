# 静的調査 Infrastructure+Presentation+Remote(未検証の仮説)
- I-1 Remote: ContractMapper.ToModel の検証例外が WeatherProviderException に変換されず漏れる(RemoteWeatherService.cs:99-106 の catch はデッドコード)
- I-2 背景取得: NWS が NotFound のとき WeatherService が MET にフォールバックし、AllowsBackgroundFetch=true のまま背景で MET を取得(不変条件 6 違反)
- I-3 NWS の日最高/最低を現地日付でなく UTC 日付で集計(ObservationHistory.cs:288)
- I-4 HTTP キャッシュの LastAccessedAt が hit で更新されない(NeedsAccessUpdate 未使用)
- I-5 HistoryViewModel: (a) NWS の降水棒が空 (b) 今日の最高/最低が昨日 (c) 古い Message (d) 範囲切替の競合
- I-6 お気に入りの重複登録 / I-7 地図の時間軸が前面で更新されない / I-8 PlaceWeatherViewModel のキャンセル・同時更新
- 仮説: Units.Temperature の "-0°"、キャッシュ dir 消失、通知の stale/同キー、Remote の Location 非照合 ほか
