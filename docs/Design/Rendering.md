# SkiaSharp による描画設計

## 決定事項

| 論点 | 決定 | 根拠 |
|---|---|---|
| SkiaSharp の版 | 4.x(2026-10-04 時点の安定版 4.153.1)を Central Package Management で一元固定 | 最新の安定版。グラフ・地図も自前描画にしたため、3.x 依存のライブラリと混在しない |
| グラフ | `Weather.Rendering.Charts` で自前描画 | LiveCharts2 2.0.5 は SkiaSharp.Views.Maui.Controls 3.119 に依存し、4.x(古い API を削除済み)と混ぜると衝突のおそれ。シーンと見た目を統一できる |
| 世界地図 | `Weather.Rendering.Map` で Natural Earth のオフラインベクターを自前描画 | オフラインで動く。OSM 公式タイルは容量が限られ大量利用はブロック対象。MAUI Maps は Android で Google Maps の API キーが必要 |
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
├─ Map/        WorldMapRenderer(投影・パン/ズーム・マーカー・ヒットテスト)
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
| iOS | 自作ハンドラー `SceneMetalViewHandler` → `SKMetalView` | MTKView の `PreferredFramesPerSecond` | `SKGLView` → `SKCanvasView` |
| SceneLab(WPF) | `WriteableBitmap` のバックバッファに SKSurface で直接描画(CPU)。SkiaSharp.Views.WPF 4.153.1 は .NET Framework 向けのみ(NU1701)のため使わない | CompositionTarget.Rendering | — |

- iOS の自作ハンドラーは最初のスパイク(試作)で検証する(**要確認**: 連続描画、ProMotion 120 Hz での扱い、背景移行時の停止)。
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

## 世界地図(Weather.Rendering.Map)

- Geo の countries.bin(Natural Earth 国境)と jp-areas.bin(気象庁の区域)を描く。地図データを二重に持たない。
- 正距円筒図法で、ピンチによるズームとパンに対応する。ズームに応じて GeoNames の主要都市名を表示する(人口のしきい値をズームで変える)。
- タップ位置 → GeoPoint → `ILocationResolver` で地点を解決する。お気に入りをマーカーで表示する。
- 道路など詳細な情報は持たない。細かい地点は検索で選ぶ(天気の地点選択には十分)。

## 文字の書体

SkiaSharp 4 では書体の指定が必須。グラフ・地図のラベルは `SKFontManager.Default.MatchCharacter` で端末の CJK 書体を取得して使い、フォントは同梱しない。取得できない場合に備え、`FontProvider` で一元管理する。

## 不変条件・テスト

- 全レイヤー × 全品質段階で、オフスクリーンのラスター描画が例外なく完了し、出力に NaN 由来の破綻がない(スモークテスト)
- 代表的なシーン(快晴昼・夕焼け・曇天・雷雨夜・大雪・濃霧)のゴールデン画像を、許容誤差付きで比較する
- シェーダーのコンパイル失敗時に代替描画へ切り替わる
- 定常状態の 1 フレームあたりのマネージド割り当てが 4 KB 未満(粒子のバッファは使い回す。シェーダーやグラデーションのラッパーなど小さな割り当ては許容)
