using System.Globalization;

namespace Weather.Presentation;

public enum UnitSystem{Metric,Imperial}
public enum RenderQuality{Auto,Low,Medium,High}

/// <summary>設定(Screens.md「設定」)。保存先は ISettingsStore(MAUI Preferences)。</summary>
public sealed class AppSettings(ISettingsStore store){
    private const string UnitsKey="units";
    private const string QualityKey="quality";
    private const string ReduceFlashesKey="reduceFlashes";
    private const string PowerSavingKey="powerSaving";
    private const string HistoryRetentionKey="historyRetentionDays";
    private const string AlertNotificationsKey="alertNotifications";

    public event EventHandler? Changed;

    /// <summary>既定は端末の地域で決める(米国なら Imperial)。</summary>
    public UnitSystem Units{
        get{
            if(Enum.TryParse<UnitSystem>(store.Get(UnitsKey),out var value)){
                return value;
            }
            return DefaultUnits(RegionInfo.CurrentRegion.TwoLetterISORegionName);
        }
        set=>this.Set(UnitsKey,value.ToString());
    }

    public RenderQuality Quality{
        get{
            if(Enum.TryParse<RenderQuality>(store.Get(QualityKey),out var value)){
                return value;
            }
            return RenderQuality.Auto;
        }
        set=>this.Set(QualityKey,value.ToString());
    }

    public bool ReduceFlashes{
        get=>store.Get(ReduceFlashesKey)=="1";
        set=>this.Set(ReduceFlashesKey,Flag(value));
    }

    /// <summary>省電力(30 fps)。</summary>
    public bool PowerSaving{
        get=>store.Get(PowerSavingKey)=="1";
        set=>this.Set(PowerSavingKey,Flag(value));
    }

    /// <summary>観測履歴の保持期間(既定 1 年。30 日 / 1 年 / 3 年)。</summary>
    public int HistoryRetentionDays{
        get{
            if(int.TryParse(store.Get(HistoryRetentionKey),NumberStyles.Integer,CultureInfo.InvariantCulture,out var days)&&days>0){
                return days;
            }
            return 365;
        }
        set=>this.Set(HistoryRetentionKey,value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>警報の通知(既定は無効。有効にするときに通知の許可を求める)。</summary>
    public bool AlertNotifications{
        get=>store.Get(AlertNotificationsKey)=="1";
        set=>this.Set(AlertNotificationsKey,Flag(value));
    }

    public static UnitSystem DefaultUnits(string regionCode){
        if(regionCode is "US" or "LR" or "MM"){
            return UnitSystem.Imperial;
        }
        return UnitSystem.Metric;
    }

    private void Set(string key,string value){
        store.Set(key,value);
        this.Changed?.Invoke(this,EventArgs.Empty);
    }

    private static string Flag(bool value){
        if(value){
            return "1";
        }
        return "0";
    }
}
