using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weather.Remote.Contracts;

/// <summary>電文の JSON(source generator。リフレクションを使わないので AOT・トリミングでも動く)。</summary>
public static class RemoteJson{
    public static string Serialize<T>(T value){
        return JsonSerializer.Serialize(value,typeof(T),RemoteJsonContext.Default);
    }

    public static T Deserialize<T>(string json){
        var value=JsonSerializer.Deserialize(json,typeof(T),RemoteJsonContext.Default);
        if(value is not T typed){
            throw new JsonException($"{typeof(T).Name} を読み取れません。");
        }
        return typed;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter=true,
    //OutOfCoverage の距離は無限大(区域が 20 km 以内にない)。JSON の数値には書けないので "Infinity" の文字列で往復する
    NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,
    //サーバーの応答は信頼しない: 非 null の項目への null は JsonException にして InvalidResponse にする(null を省略する項目は nullable のまま)
    RespectNullableAnnotations=true,
    DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResolveResponse))]
[JsonSerializable(typeof(ForecastResponse))]
[JsonSerializable(typeof(AlertResponse))]
[JsonSerializable(typeof(StationsResponse))]
[JsonSerializable(typeof(ObservationSeriesDto))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class RemoteJsonContext:JsonSerializerContext;
