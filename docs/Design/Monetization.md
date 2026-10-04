# 広告と課金

無料で使えるようにし、広告(AdMob のバナー)で収益を得る。買い切りの課金で広告を消せる。初回のストア公開から広告ありで出す(公開後に「無料→広告あり」へ変えると反発を招きやすいため)。

## 決定事項(2026-10-04 ユーザーの決定)

| 論点 | 決定 | 根拠 |
|---|---|---|
| 収益モデル | 広告+買い切り(非消耗型)の「広告を消す」 | 天気アプリで一般的。サブスクより状態管理が単純 |
| 形式・場所 | バナーのみ。地図(トップ)の最下部と地点の詳細の最下部 | 操作を中断しない。全画面広告は天気をすぐ見たい利用者の不満が大きい |
| 個人化 | 同意した人だけ。iOS は ATT、EEA・英国等は Google UMP の同意フォーム。拒否なら非個人化 | 収益と法令(GDPR 等)・ストアの規約の両立 |
| 時期 | 初回リリースに含める | 上記 |
| ネットワーク | Google AdMob のみ(メディエーションなし) | iOS・Android 両対応で UMP も提供。メディエーションは各社の SDK のバインディングが必要で負担が大きい(後から追加可能) |
| 組み込み方 | 自前の薄いラッパー | 下記 |

### 組み込み方の選定

MAUI 向けの公式 SDK はない。定番の Plugin.MauiMTAdmob(2.4.0、.NET 10 対応)は UMP が有料ライセンス版のみのため使わない。課金の Plugin.InAppBilling は作者が保守終了を表明しているため使わず、Microsoft の BillingService サンプル(dotnet/maui-samples 10.0)と同じく OS ごとに実装する。

| | Android | iOS |
|---|---|---|
| 広告 | Xamarin.GooglePlayServices.Ads(Microsoft が保守するバインディング) | Google 公式の Swift Package を包む Swift のラッパー(Native Library Interop。試作 A2) |
| 同意 | Xamarin.Google.UserMessagingPlatform | 同上(UMP)+ ATT |
| 課金 | Xamarin.Android.Google.BillingClient(Play Billing 9) | 同上のラッパーに StoreKit 2 を入れる(StoreKit 1 は非推奨) |

試作が失敗した場合の代替: 広告は Plugin.MauiMTAdmob+有料ライセンス。

## 画面

- バナーは `AdBannerView`(App の Controls)。地図・地点の詳細のページの Grid の最下部の行に置き、地図のキャンバスには重ねない(方針 7 の出典の帯・時間軸・凡例を覆わない。AdMob の誤タップ防止の規定にも合う)。広告を出さないとき(購入済み・同意なし・未対応の OS)は高さ 0。
- 警報・警報の詳細・設定・データと出典・検索・地点の管理、ウィジェット、通知には広告を出さない(`AdBannerView` を置かないことで保証する)。
- 設定画面の「広告」の欄: 広告を消す(ストアの価格を表示。価格を取得できなければボタンを出さない)、購入を復元、広告のプライバシー設定(UMP が必要とする地域だけ表示。EEA では必須)。購入済みならその旨だけを表示する。

## 構成

| 場所 | 内容 |
|---|---|
| Presentation `Monetization/` | 抽象 `IAdPlatform`(同意・SDK 初期化・プライバシー設定)、`IPurchasePlatform`(商品・購入・所有の確認・復元)、何もしない実装 `NullMonetizationPlatform`、判断 `AdPolicy` |
| Presentation `ViewModels/AdSettingsViewModel` | 設定画面の「広告」の欄 |
| App `Controls/AdBannerView` | バナー。`AdPolicy.ShowBanner` を見て OS のバナー(`NativeBanner`)を作る・破棄する |
| App `Platforms/Android/Monetization/` | `AndroidAdPlatform`(UMP・Mobile Ads)、`AndroidPurchasePlatform`(Play Billing)、`NativeBannerHandler`(AdView) |
| App `Services/AdUnits` | 広告ユニット ID(csproj が AssemblyMetadata に書き込む) |

### AdPolicy

- `ShowBanner = 購入済みでない && 広告を要求できる(UMP の canRequestAds かつ SDK 初期化済み)`。
- 購入済みかは設定(`AppSettings.AdFree`)にキャッシュする。起動直後・オフラインでも購入済みなら広告を出さない。
- 起動の流れ(`StartAsync`。地図の表示後に 1 回): ストアで所有を確認(問い合わせられなければキャッシュを維持。所有していなければ返金・取消としてキャッシュを消す)→ 購入済みでなければ同意を確認(UMP の情報の更新 → 必要な地域だけ同意フォーム → iOS は ATT)→ 広告を要求できるなら SDK を初期化 → バナーを出す。同意の確認が終わるまで広告を要求しない。購入済みなら同意フォーム・ATT も出さない。
- 購入: 完了なら即座にバナーを消す。保留(コンビニ払い等)・取り消し・失敗では消さない(保留は次回起動時の所有の確認で反映)。

## ガードレール

1. 購入済みならバナーを作らず、広告を要求しない(AdPolicy のテスト)。
2. 広告を置く画面は地図と地点の詳細だけ(上記)。
3. バナーは地図・出典の帯に重ねない(別の行)。
4. 広告の要求に端末の位置情報を渡さない。
5. Debug は Google のテスト用 ID。Release は環境変数 `WEATHER_ADMOB_ANDROID_APP_ID` / `WEATHER_ADMOB_ANDROID_BANNER_ID` / `WEATHER_ADMOB_IOS_APP_ID` / `WEATHER_ADMOB_IOS_BANNER_ID` で本番の ID を渡し、未設定またはテスト用 ID ならビルドを失敗させる(csproj の CheckAdMobIds)。Release をエミュレーターで試すときだけ `-p:UseTestAds=true`(警告が出る)。
6. Presentation は広告・課金の SDK を参照しない(アーキテクチャテスト)。

## Android の依存関係(試作 A1 の結果、2026-10-04)

- Google Mobile Ads のバインディングは 124.6.0 以降のどの版も、MAUI 10.0.110(最新)が使う AndroidX(Lifecycle 2.9.2 等)と、アプリが固定していた WorkManager 2.10.3 より新しい版(Lifecycle 2.9.3〜2.11・Work 2.10.4〜2.11.2 等)を要求する。MAUI 側を上げる手段はないため、MAUI が引き込む古い AndroidX(Lifecycle の LiveData・Process・Service・ktx、Fragment.Ktx、AppCompat、Collection.Ktx、Tracing.Ktx)を Directory.Packages.props で明示的に新しい版に揃えた。Debug と Release(R8)のビルドで重複クラス・警告がないことを確認した。
- 以前 WorkManager を 2.10.3 に固定していた理由(新しい AndroidX でクラスが重複)は、上記のとおり AndroidX 全体を揃えることで解消したため、2.11.2.1 に上げた。
- androidx.webkit 1.17(広告 SDK が依存)が minSdk 24 を要求するため、Android の最低対応を 6.0(API 23)から 7.0(API 24)に上げた。
- MAUI を更新するときは、この揃えた版が MAUI の要求より古くならないか確認する(古くなったら NU1605 のダウングレード警告が出る)。

## 要確認事項

- Google Mobile Ads SDK が独自に位置情報を使うか(アプリは位置の権限を持つ。プライバシーポリシーに反映する)。
- Play の静的なテスト用商品(`android.test.purchased`)は廃止されたため、購入の確認には Play Console の内部テストとライセンステスターが必要。
- iOS の Native Library Interop で、Windows 上の net10.0-ios の C# コンパイルを壊さない方法(試作 A2)。

## ユーザーの作業(公開前)

- AdMob のアカウント・アプリ・広告ユニットの作成、支払い・税情報の登録、UMP の同意メッセージ(GDPR・IDFA の説明)の作成。
- Play Console のアプリ内アイテム(ID `remove_ads`)、App Store Connect の非消耗型 IAP(同じ ID)と有料 App 契約、価格の決定。
- app-ads.txt をストアに登録する開発者サイトの直下に置く(設置先は未定)。
