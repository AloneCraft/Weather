using Weather.Core;

namespace Weather.Providers.Jma;

internal sealed record JmaWarningCode(string Name,string Element,AlertTier Tier,int? WarningLevel);

/// <summary>気象庁の警報コード(r8)→ 名称・Tier・警戒レベル(WeatherProviders.md 付録 C)。</summary>
internal static partial class JmaWarningCodes{
    public static IReadOnlyCollection<string> Codes=>Table.Keys;

    public static JmaWarningCode? Find(string? code){
        if(code is null){
            return null;
        }
        Table.TryGetValue(code,out var value);
        return value;
    }
}
