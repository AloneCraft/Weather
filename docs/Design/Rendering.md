# SkiaSharp による描画設計

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| SkiaSharp の版 | 4.x(2026-10-04 時点の安定版 4.153.1)を Central Package Management で一元固定 | 最新の安定版。グラフ・地図も自前描画にしたため、3.x 依存のライブラリと混在しない |
| グラフ | `Weather.Rendering.Charts` で自前描画 | LiveCharts2 2.0.5 は SkiaSharp.Views.Maui.Controls 3.119 に依存し、4.x(古い API を削除済み)と混ぜると衝突のおそれ。シーンと見た目を統一できる |
| 地図(トップ画面) | `Weather.Rendering.Map` で自前描画。Web メルカトル、背景は Natural Earth のオフラインベクター、上に GFS の格子・気象庁のタイル・風の粒子(2026-10-04 にトップを地図へ変更) | 気象庁の XYZ タイルと重ねるため Web メルカトル。オフラインで動く背景。OSM 公式タイルは大量利用はブロック対象。MAUI Maps は Android で Google Maps の API キーが必要 |
| iOS の描画ホスト | SkiaSharp.Views.iOS の `SKMetalView` を包む自作 MAUI ハンドラー | MAUI 標準の `SKGLView` は iOS で GLKView(OpenGL ES、Apple が非推奨)を使う。4.153.1 の iOS 用 SkiaSharp.Views には `SKMetalView`(MTKView)がある |
| Android の描画ホスト | `SKGLView`(OpenGL ES) | MAUI 用ハンドラーが 4.153.1 に含まれる |

## 観測事実(2026-10-04)

- SkiaSharp.Views.Maui.Core / Controls 4.153.1 の Android・iOS 用 DLL に `SKCanvasView` / `SKGLView` とそのハンドラーがある。Android には `SKGLTextureView` もある。
- iOS 用 MAUI ハンドラーの `SKGLView` は GLKView を参照する。SkiaSharp.Views.iOS 4.153.1 には `SKMetalView`・`GRMtlBackendContext` がある。
- SkiaSharp 4.0 は古い API を削除し、`SKPaint` のテキスト関連メンバーはコンパイルエラーになる。引数なしの `SKFont` は空の書体なので、書体を明示する必要がある。

## 構成

```
Weather.Rendering/
├─ Scene/      SceneRenderer, ISceneLayer, 各レイヤー, Particles/, Shaders/(.sksl を埋め込みリソースに)
├─ Charts/     TemperatureChart, PrecipitationChart, WindChart, ObservationChart
├─ Map/        MapRenderer(重ね順)、MapCamera(Web メルカトル)、BaseMapLayer、ScalarFieldLayer・JapanOverlay・IsobarLayer、
│              TileLayer、WindParticleLayer、ArrowLayer、MapResources(凡例 → パレット・日本周辺のマスク)、Shaders/
└─ Common/     Palette, QualityTier, FontProvider, FrameStats
```

App 側(MAUI)は描画ホストとフレームループだけを持つ。

```csharp
namespace Weather.Rendering.Scene;

public enum QualityTier{Low,Medium,High}

public readonly record struct SceneFrame(SceneState State,SKSizeI Size,double TimeSeconds,double DeltaSeconds,QualityTier Tier);

public interface ISceneLayer:IDisposable{
    void Update(in SceneFrame frame);
    void Draw(SKCanvas canvas,in SceneFrame frame);
}

public sealed class SceneRenderer:IDisposable{
    public void SetTarget(SceneState target){ ... }                    //目標状態。内部で滑らかに追従
    public void Render(SKCanvas canvas,SKSizeI size,double timeSeconds){ ... }
    public FrameStats Stats{get;}                                      //自動品質調整用
    public void Dispose(){ ... }
}
```

## 描画ホストとフレームループ(MAUI)

| 環境 | ホスト | ループ | 代替 |
|---|---|---|---|
| Android | `SKGLView` | 60 fps は `HasRenderLoop=true`(4.153.1 で存在を確認)、省電力の 30 fps は Dispatcher のタイマーで `InvalidateSurface` | `SKCanvasView`(CPU) |
| iOS | 自作ハンドラー `MetalCanvasViewHandler` → `SKMetalView` | MTKView の `PreferredFramesPerSecond`。止めている間は `EnableSetNeedsDisplay` + `SetNeedsDisplay` で 1 回だけ描く | `SKGLView` → `SKCanvasView` |
| SceneLab(WPF) | `WriteableBitmap` のバックバッファに SKSurface で直接描画(CPU)。SkiaSharp.Views.WPF 4.153.1 は .NET Framework 向けのみ(NU1701)のため使わない | CompositionTarget.Rendering | — |

- 両 OS とも `AnimatedCanvas`(App)にまとめ、描画の中身は `IFrameRenderer`(空のシーンは SceneHost、地図は MapHost)で差し替える。`IsRunning` の間は連続描画、止めている間は `Invalidate()` で 1 回描く。
- iOS の自作ハンドラーは CI(macOS)でビルドを確認済み。実機での連続描画・ProMotion 120 Hz・背景移行時の停止は**要確認**。
- ページが見えない、アプリが背景にある(`Window.Stopped`)、「視差効果を減らす」が有効、のいずれかでループを止める(または低頻度にする)。
- SceneLab は CPU 描画のため、見た目の調整用であり性能測定には使わない。

## レイヤー構成(奥から手前)

| # | レイヤー | 内容 | 技法 |
|---|---|---|---|
| 1 | Sky | 太陽高度に応じた空のグラデーション(昼・薄明・夜) | 太陽高度のキーフレームから CPU で色を補間し、線形グラデーションのシェーダーで描く(SkSL は使わない。3 色で十分なため) |
| 2 | Stars | 星(薄明で減衰) | 事前生成した点群を `DrawPoints`。明るさだけ変える |
| 3 | Moon | 月(位相) | 円+位相マスク(SkSL) |
| 4 | Sun | 太陽円盤と光暈。雲量で減光 | 放射グラデーション |
| 5 | FarClouds | 遠景の雲。被覆・暗さ・風で流れる | SkSL の fBm ノイズ。低解像度オフスクリーンに描いて拡大 |
| 6 | Landscape | 地平の稜線・街並みのシルエット | 起動時に `SKPicture` に記録して再利用 |
| 7 | NearClouds | 近景の雲・積乱雲。稲光で内部が光る | SkSL(FarClouds と同じシェーダーを別パラメーターで) |
| 8 | Precipitation | 雨・雪・あられ・みぞれ。風で傾く。奥行き 3 層の視差 | パーティクル |
| 9 | Fog / Haze | 霧・もや・煙・砂塵 | SkSL の低周波ノイズ+高さによる減衰 |
| 10 | Lightning | 稲妻の経路と全画面の閃光 | 中点変位法で経路を生成(シード固定)、加算合成 |
| 11 | Foreground | 地面の跳ね返り(High のみ) | パーティクル |

文字(気温・地名など)はキャンバスに描かず、MAUI のコントロールを上に重ねる(アクセシビリティとテキスト品質のため)。

## SkSL シェーダー

- `.sksl` ファイルを埋め込みリソースにし、起動時に 1 回だけコンパイルしてキャッシュする。uniform だけを毎フレーム更新する。
- コンパイルに失敗した場合は、そのレイヤーを代替描画(単色グラデーションや単純なスプライト)に切り替えて診断ログに記録する。アプリは落とさない。
- API(4.153.1 で確認済み): `SKRuntimeEffect.CreateShader(sksl, out errors)` で effect を作り、レイヤーごとの `SKRuntimeEffectUniforms` に値を設定して `effect.ToShader(uniforms)` で生成する。`SKRuntimeShaderBuilder` は破棄時に共有の effect まで破棄する(ハンドルが 0 になり次フレームでネイティブ例外)ため使わない。CPU(ラスター)描画でも動作する。
- 精度: ハッシュ関数は `float`(高精度)で計算する。モバイル GPU の `half` では縞が出る。

雲シェーダーの骨格:

```glsl
uniform float2 uResolution;
uniform float uTime;
uniform float uCover;      // 0..1
uniform float uDarkness;   // 0..1
uniform float2 uWind;      // 画面座標系の流れ(風向・風速から算出)

float hash(float2 p){ return fract(sin(dot(p,float2(127.1,311.7)))*43758.5453); }

float noise(float2 p){
    float2 i=floor(p);
    float2 f=fract(p);
    float2 u=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+float2(1,0)),u.x),mix(hash(i+float2(0,1)),hash(i+float2(1,1)),u.x),u.y);
}

float fbm(float2 p){
    float v=0.0;
    float a=0.5;
    for(int i=0;i<5;i++){    // オクターブ数は品質段階で 3 / 4 / 6 に切り替える(別シェーダーとしてコンパイル)
        v+=a*noise(p);
        p*=2.0;
        a*=0.5;
    }
    return v;
}

half4 main(float2 coord){
    float2 uv=coord/uResolution.y;
    float n=fbm(uv*3.0+uWind*uTime);
    float d=smoothstep(1.0-uCover,1.0-uCover+0.25,n);
    float3 col=mix(float3(1.0),float3(0.35),uDarkness);
    return half4(half3(col*d),half(d));    // 乗算済みアルファ
}
```

## パーティクル

- 構造体の配列ではなく、成分ごとの配列(SoA: `float[] x, y, vx, vy, life`)を固定容量で事前確保する。**フレームごとにメモリ確保をしない**(モバイルの GC による引っかかりを避ける)。
- 発生量 = 降水強度 × 品質段階の上限 × 断続性ゲート。ゲートはシード固定の低周波ノイズで、断続性の割合に応じて開閉する。
- 雨: 速度に比例した長さの線分。傾きは風から決める。事前確保した `SKPoint[]` に書き込み、`canvas.DrawPoints(SKPointMode.Lines, ...)` で描く。
- 雪: 事前生成した結晶スプライトを、`DrawAtlas` で揺らぎながら描く。
- みぞれ: 雨と雪を混ぜる。あられ・凍雨: 小さな粒が跳ねる。
- 奥行き 3 層(速度・大きさ・不透明度を変える)で視差を出す。

## 稲光とアクセシビリティ

- 頻度は雷活動度に比例させる。閃光の包絡は 0.1〜0.3 秒。
- 光過敏への配慮: 閃光は **1 秒に 3 回以下**(WCAG 2.3.1)とし、輝度変化を抑える。設定に「稲光の点滅を抑える」を用意し、OS の「視差効果を減らす」が有効なら既定で抑える。

## 状態の遷移

- 目標状態が変わったら、各連続値を臨界減衰ばねで 2〜3 秒かけて追従させる。
- 降水種別などの分類値は、旧種別の発生を止めて新種別を立ち上げる(クロスフェード)。

## 性能

| 項目 | 方針 |
|---|---|
| 目標 | 60 fps(16.6 ms)。シーンの描画時間の目安は中位機種で 8 ms 以内(実機で**要確認**) |
| 省電力 | 端末の省電力モードや発熱状態が高いときは 30 fps に下げる。非表示時は停止 |
| 品質段階 | Low / Medium / High: 雲の描画解像度 0.25 / 0.5 / 0.75、ノイズのオクターブ 3 / 4 / 6、雨の上限 600 / 1500 / 3000、雪の上限 300 / 800 / 1500、跳ね返りは High のみ |
| 自動調整 | 平均フレーム時間が 3 秒間 20 ms を超えたら 1 段下げる。30 秒間 12 ms 未満なら 1 段上げる |
| メモリ確保 | 毎フレームの確保をしない。`SKPaint` / `SKPath` / 配列 / オフスクリーン `SKSurface` を再利用する(サイズ変更時のみ作り直す)。描画経路で LINQ を使わない |
| 静的な内容 | 稜線などの変化しない内容は `SKPicture` / `SKImage` に記録して再利用 |
| 計測 | デバッグ版で FPS とフレーム時間を重ねて表示する。Android GPU Inspector / Xcode Instruments でプロファイルする |

## グラフ(Weather.Rendering.Charts)

- 気温の折れ線(滑らかなスプライン)、降水量の棒、降水確率の面、風向の矢印、観測履歴用の時系列。
- 時間軸は地点のタイムゾーンで描く。タップ・ドラッグ位置から時刻を返すヒットテストを持ち、シーンの時間軸操作と連動する。
- 色はシーンと共通の Palette を使う。

## 地図(Weather.Rendering.Map)

トップ画面の Windy 型の地図。データは `IMapDataService`(Providers の MapDataService)が層と時刻ごとに `MapFrame` で渡す(WeatherProviders.md「地図のデータ」)。

### 投影とカメラ

- Web メルカトル(世界の座標は経度 −180〜180° が x 0〜1、北が y 0)。気象庁の XYZ タイルとそのまま重なる。ズーム 1.5〜11。
- `MapCamera` は UI のスレッドで操作し、描画は `Snapshot()` の不変の `MapView` を使う(Android の SKGLView は描画が別スレッド)。
- 画素の細かさ(PixelRatio)をカメラに持たせ、線の太さ・文字・粒子の速さ・タイルのズームの選び方を端末の密度に合わせる。

### 重ね順(奥から手前)

| # | 層 | 内容 |
|---|---|---|
| 1 | 背景の地図(陸) | Natural Earth の国(ズームで 3 段階に簡略化)。海の色で塗りつぶした上に陸を塗る |
| 2 | 格子の色 | GFS の風速・降水強度・気温・雲量。SkSL(`field.sksl`)で画素ごとに緯度経度 → 格子座標を求め、線形補間してパレットで着色 |
| 3 | 気象庁のタイル | 配色を変えずにそのまま描く(画素は補間しない)。`SaveLayer` の中で描き、日本周辺のマスクで `DstIn` して日本周辺だけに残す |
| 4 | 予報期間外の斜線 | 気象庁の予報期間を過ぎた日本周辺(GFS で埋めないことを示す) |
| 5 | 等圧線 | 海面気圧のマーチングスクエア(4 hPa ごと、20 hPa ごとに太線、暗い縁取り)。格子が変わったときだけ線分を作り、世界の座標のパスにしておく |
| 6 | 風の粒子 | 画面座標の粒子を、日本周辺の外は GFS の U・V、日本周辺(マスクの中)は気象庁の海上分布予報から作った海上の U・V(`JapanWind`)で動かし、画面外のサーフェスに軌跡を描いて毎フレーム薄める |
| 7 | 日本周辺の点 | アメダスの風の矢印(風速で色分け)と気温(ズーム 6 以上)、海上分布予報の風向(8 方位)。日本周辺の外は描かない |
| 8 | 海岸線・境界・地名 | 国境(白)、ズーム 7 以上で気象庁の市町村境界、GeoNames の主要都市名(縁取り付き、重なりを間引く) |
| 9 | ピン・選択地点 | お気に入り・現在地(公式予報の気温)、タップした地点 |

### 格子のテクスチャとパレット

- 格子は RgbaF16 の画像(r = 0〜1 に正規化した値、a = 有効)。欠損・日本周辺の NaN は透明。乗算済みのまま線形補間し、r / a を値、a を被覆率として使う(境界がなめらかになる)。
- 正規化(`FieldEncoding`): 気温 −45〜50 ℃、風速 0〜40 m/s、雲量 0〜100 %、降水は対数(0〜150 mm/h。弱い雨の区分を見分けるため)。
- パレット(256 色)は凡例(`MapLegends`)から作る。降水・気温は気象庁の凡例と同じ区分・色(継ぎ目をそろえる)、風速・雲量は連続的な配色。GFS の 1 mm/h 未満の雨は半透明(暗い地図の上で白い面が広がりすぎないため)。
- 経度方向に一周する格子は `Repeat` で標本化し、日付変更線をまたいでも補間がつながる。

### 日本周辺(方針 5)

- マスクは Geo の 0.05° のビットマスク(地点の解決と同じ範囲)を画像にしたもの。格子レイヤーのシェーダー、気象庁のタイルの切り抜き、粒子の消去、矢印の間引きで共通に使う。
- GFS の格子は Providers で日本周辺を NaN にしてあり、描画でもマスクで透明にする(二重)。吹き出しは Presentation が日本周辺かを判定して GFS の値を出さない(三重)。

### 風の粒子

- 数は品質段階と画面の広さで決める(1 メガピクセルあたり 4,000 / 8,000 / 13,000、300〜6,000)。寿命 40〜110 フレームで配り直す。
- 速さは風速 1 m/s あたり 3.2 論理画素/秒。カメラ・風の格子が変われば軌跡を消して配り直す。
- メモリ確保: 粒子と線分のバッファは数が変わったときだけ作る。`DrawPoints` は配列全体を描くため、使わない枠は画面外の長さ 0 の線にする。
- 日本周辺(マスクの中)は `JapanWind` だけで動かし、GFS を使わない(方針 5)。`JapanWind` は補間せず、最も近い区画の値をそのまま使う(気象庁の格子点値の空間内挿は予報業務の許可が要るため。WeatherProviders.md)。`JapanWind` がない場所(陸上・海上分布予報の範囲外)では粒子を消す。GFS がなく `JapanWind` だけのコマでも粒子を動かす。
- 「視差効果を減らす」が有効なら粒子を止め、画面の格子ごとの静止した矢印にする(日本周辺は海上分布予報の矢印)。
- 連続描画は粒子があるときだけ。それ以外の層はカメラ・コマ・タイルの読み込みのたびに 1 回描く。

### 気象庁のタイル

- 表示のズーム + log2(PixelRatio) に近い、画像が実在するズーム(雨雲・今後の雨・天気分布予報は偶数ズーム 4〜10)を読む。読めていないタイルは、読み込み済みの低いズームのタイルの該当部分を拡大して代わりに描く。
- 読み込みは非同期(HTTP キャッシュを通す)で、メモリには最大 192 枚。層が変わったら読み込み中の要求を取り消す。

### 試作の結果(2026-10-04)

| # | 試作 | 結果 |
|---|---|---|
| P1 | GRIB2 の復号 | データ表現テンプレート 5.3(複合圧縮 + 空間差分)を外部ライブラリなしで復号。0.5° と 1° の同じ格子点の値が圧縮精度の範囲で一致(海面気圧 0.8 Pa、気温 0.07 K、風 0.006 m/s)。0.5° の 1 要素の復号は約 2 ms(PC)。降水強度は 1° の方が別の内挿のため局所的に違う |
| P2 | 気象庁のタイル | URL の形・ズーム・凡例の色を jmatile の設定(table/*.properties.xml と凡例の SVG)から確認(WeatherProviders.md)。海上分布予報の風速タイルは 25 kt 未満も淡い色で海域全体を塗るため使わない |
| P3 | 性能 | Android エミュレーター(Pixel 7 相当・Android 16、ホストの GPU)で、風の層(格子 + 粒子 + 基図)が 10 秒間に 585 フレーム(約 58.5 fps、遅延 2.7 %)。中位機種の実機は未確認 |
| P4 | 位置合わせ | 日本の陸地だけを塗った合成タイル(ズーム 4)が内陸・沖合・朝鮮半島で正しい位置に出ることをテストで確認 |

## 文字の書体

SkiaSharp 4 では書体の指定が必須。グラフ・地図のラベルは `SKFontManager.Default.MatchCharacter` で端末の CJK 書体を取得して使い、フォントは同梱しない。取得できない場合に備え、`FontProvider` で一元管理する。

## 不変条件・テスト

- 全レイヤー × 全品質段階で、オフスクリーンのラスター描画が例外なく完了し、出力に NaN 由来の破綻がない(スモークテスト)
- 代表的なシーン(快晴昼・夕焼け・曇天・雷雨夜・大雪・濃霧)のゴールデン画像を、許容誤差付きで比較する
- シェーダーのコンパイル失敗時に代替描画へ切り替わる
- 定常状態の 1 フレームあたりのマネージド割り当てが 4 KB 未満(粒子のバッファは使い回す。シェーダーやグラデーションのラッパーなど小さな割り当ては許容)。地図は 16 KB 未満(地名の文字列の計測など)
- 地図: 日本周辺に GFS の色が出ない・GFS の粒子が入らない(日本周辺の海上は気象庁の海上の風で粒子が流れる)・等圧線を引かない。気象庁のタイルが Web メルカトルの正しい位置に出て、日本周辺の外は描かない。代表の地図(気温 + 等圧線 + 予報期間外の斜線、風)のゴールデン画像
