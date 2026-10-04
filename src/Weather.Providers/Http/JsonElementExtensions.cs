using System.Globalization;
using System.Text.Json;

namespace Weather.Providers.Http;

/// <summary>JsonElement の読み取り補助。リフレクションを使わないため AOT / トリミングで安全。</summary>
internal static class JsonElementExtensions{
    public static JsonElement? Prop(this JsonElement element,string name){
        if(element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind!=JsonValueKind.Null){
            return value;
        }
        return null;
    }

    public static JsonElement Required(this JsonElement element,string name){
        if(element.Prop(name) is {} value){
            return value;
        }
        throw new KeyNotFoundException($"必須項目がありません: {name}");
    }

    public static string? Str(this JsonElement element,string name){
        if(element.Prop(name) is {} value){
            return value.AsString();
        }
        return null;
    }

    public static string? AsString(this JsonElement element){
        if(element.ValueKind==JsonValueKind.String){
            return element.GetString();
        }
        if(element.ValueKind==JsonValueKind.Number){
            return element.GetRawText();
        }
        return null;
    }

    /// <summary>数値、または数値を表す文字列を読む。空文字・null は null。</summary>
    public static double? AsDouble(this JsonElement element){
        if(element.ValueKind==JsonValueKind.Number){
            return element.GetDouble();
        }
        if(element.ValueKind==JsonValueKind.String){
            var text=element.GetString();
            if(!string.IsNullOrWhiteSpace(text)&&double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)){
                return value;
            }
        }
        return null;
    }

    public static double? Num(this JsonElement element,string name){
        if(element.Prop(name) is {} value){
            return value.AsDouble();
        }
        return null;
    }

    public static bool? Bool(this JsonElement element,string name){
        if(element.Prop(name) is {} value){
            if(value.ValueKind==JsonValueKind.True){
                return true;
            }
            if(value.ValueKind==JsonValueKind.False){
                return false;
            }
        }
        return null;
    }

    public static DateTimeOffset? Time(this JsonElement element,string name){
        var text=element.Str(name);
        if(text is not null&&DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var value)){
            return value;
        }
        return null;
    }

    public static IEnumerable<JsonElement> Items(this JsonElement element,string name){
        if(element.Prop(name) is {ValueKind:JsonValueKind.Array} array){
            return array.EnumerateArray();
        }
        return [];
    }

    public static IReadOnlyList<JsonElement> ItemList(this JsonElement element,string name){
        return [..element.Items(name)];
    }

    /// <summary>NWS の {"unitCode": "...", "value": n} 形式。</summary>
    public static (double? Value,string? Unit,string? Quality) Quantity(this JsonElement element,string name){
        if(element.Prop(name) is {ValueKind:JsonValueKind.Object} q){
            return (q.Num("value"),q.Str("unitCode"),q.Str("qualityControl"));
        }
        return (null,null,null);
    }
}
