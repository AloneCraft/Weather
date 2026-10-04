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
    DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResolveResponse))]
[JsonSerializable(typeof(ForecastResponse))]
[JsonSerializable(typeof(AlertResponse))]
[JsonSerializable(typeof(StationsResponse))]
[JsonSerializable(typeof(ObservationSeriesDto))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class RemoteJsonContext:JsonSerializerContext;
