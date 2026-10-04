using System.Reflection;

namespace Weather.App.Services;

/// <summary>
/// AdMob の広告ユニット ID と同意のデバッグ設定(Monetization.md)。値は Weather.App.csproj が AssemblyMetadata に書き込む。
/// Debug は Google のテスト用 ID、Release は環境変数で渡した本番の ID(未設定なら Release ビルドが失敗する)。
/// </summary>
internal static class AdUnits{
    public static string Banner=>Get("AdMobBannerUnitId");

    /// <summary>同意フォームの地域を試験用に固定する(EEA / Other)。空なら固定しない。Debug でだけ設定できる。</summary>
    public static string DebugGeography=>Get("AdsDebugGeography");

    /// <summary>同意フォームの試験用端末(logcat に出るハッシュ化した ID)。</summary>
    public static string TestDeviceHashedId=>Get("AdsTestDeviceHashedId");

    private static string Get(string key){
        foreach(var attribute in typeof(AdUnits).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()){
            if(attribute.Key==key){
                return attribute.Value??"";
            }
        }
        return "";
    }
}
