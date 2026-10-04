using System.Globalization;
using Weather.Core;
using Weather.Presentation.ViewModels;

namespace Weather.App;

/// <summary>背景(シーン)が暗いか → 文字色(Screens.md「テーマ」)。</summary>
public sealed class BackgroundToTextColorConverter:IValueConverter{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        if(value is true){
            return Colors.White;
        }
        return Color.FromArgb("#1A1F2B");
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        throw new NotSupportedException();
    }
}

/// <summary>背景が暗いか → カードの背景色(半透明)。</summary>
public sealed class BackgroundToCardColorConverter:IValueConverter{
    /// <summary>
    /// カード内の文字は常に白・淡色(行のテンプレートは地点の背景に依存できない)。
    /// 明るい空(厚い雲・霧の昼)でも読めるよう、明るい背景では濃い色を強めに重ねる。
    /// </summary>
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        if(value is true){
            return Color.FromArgb("#40000000");
        }
        return Color.FromArgb("#A01A2440");
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        throw new NotSupportedException();
    }
}

/// <summary>警報の段階 → 色(特別警報・危険警報・警報・注意報の順に強く)。</summary>
public sealed class AlertTierToColorConverter:IValueConverter{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        switch(value){
            case AlertTier.Emergency:
                return Color.FromArgb("#4A148C");
            case AlertTier.Danger:
                return Color.FromArgb("#8E24AA");
            case AlertTier.Warning:
                return Color.FromArgb("#D32F2F");
            case AlertTier.Watch:
                return Color.FromArgb("#F57C00");
            case AlertTier.Advisory:
                return Color.FromArgb("#F9A825");
            default:
                return Color.FromArgb("#607D8B");
        }
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        throw new NotSupportedException();
    }
}

/// <summary>null・空文字なら false。</summary>
public sealed class HasValueConverter:IValueConverter{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        if(value is string s){
            return !string.IsNullOrWhiteSpace(s);
        }
        return value is not null;
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        throw new NotSupportedException();
    }
}

public sealed class InvertBoolConverter:IValueConverter{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        return value is not true;
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        return value is not true;
    }
}

/// <summary>状態が「表示できるデータがある」か(Ready / Stale)。</summary>
public sealed class HasDataStateConverter:IValueConverter{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        return value is PlaceState.Ready or PlaceState.Stale;
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        throw new NotSupportedException();
    }
}

/// <summary>履歴の期間(enum)↔ Picker の番号。</summary>
public sealed class HistoryRangeToIndexConverter:IValueConverter{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture){
        if(value is HistoryRange range){
            return (int)range;
        }
        return 0;
    }

    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture){
        if(value is int index&&index>=0){
            return (HistoryRange)index;
        }
        return HistoryRange.Day;
    }
}
