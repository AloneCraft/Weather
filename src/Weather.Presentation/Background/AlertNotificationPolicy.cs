using System.Globalization;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation.Background;

/// <summary>
/// 警報の通知の規則。
/// 気象業務法第 23 条により、アプリが独自に警報を作ることはできない。通知は機関が発表した警報の名称・見出しをそのまま伝え、
/// アプリ独自の危険度の判断や表現を加えない。出典(機関名・発表時刻・出典表記)を必ず含める。
/// </summary>
public static class AlertNotificationPolicy{
    /// <summary>通知する最低の段階(警報・特別警報、NWS の Warning 以上)。注意報は通知しない。</summary>
    public const AlertTier MinimumTier=AlertTier.Warning;

    public static bool ShouldNotify(Alert alert){
        ArgumentNullException.ThrowIfNull(alert);
        return alert.Status!=AlertStatus.Cancelled&&alert.Tier>=MinimumTier;
    }

    /// <summary>
    /// 同じ警報かの判定キー。NWS は更新のたびに ID が変わるため、ID ではなく種類と区域で判定する
    /// (更新では再通知せず、解除後に再び発表されたら通知する)。
    /// </summary>
    public static string Key(Alert alert){
        ArgumentNullException.ThrowIfNull(alert);
        return alert.Source.Provider+":"+(alert.EventCode??alert.EventName)+":"+alert.AreaName;
    }

    public static AlertNotification Create(string placeId,string placeName,Alert alert,TimeZoneInfo zone){
        ArgumentNullException.ThrowIfNull(alert);
        var title=string.Format(CultureInfo.CurrentCulture,Strings.AlertNotificationTitleFormat,alert.EventName,placeName);
        var lines=new List<string>{string.Format(CultureInfo.CurrentCulture,Strings.AlertNotificationBodyFormat,alert.AreaName??placeName)};
        if(!string.IsNullOrWhiteSpace(alert.Headline)){
            lines.Add(alert.Headline);
        }
        lines.Add(AttributionText.Short(alert.Source,zone));
        lines.Add(AttributionText.Credit(alert.Source));
        return new AlertNotification(Key(alert),placeId,title,string.Join("\n",lines),alert.Tier);
    }
}

/// <summary>地点ごとに通知済みの警報キーを保存する(設定の保存先を使う。件数は少ない)。</summary>
internal sealed class NotifiedAlertStore(ISettingsStore store){
    private const string Prefix="notifiedAlerts.";

    public HashSet<string> Load(string placeId){
        var value=store.Get(Prefix+placeId);
        if(string.IsNullOrEmpty(value)){
            return new HashSet<string>(StringComparer.Ordinal);
        }
        return new HashSet<string>(value.Split('\n',StringSplitOptions.RemoveEmptyEntries),StringComparer.Ordinal);
    }

    public void Save(string placeId,IReadOnlyCollection<string> keys){
        if(keys.Count==0){
            store.Set(Prefix+placeId,null);
            return;
        }
        store.Set(Prefix+placeId,string.Join("\n",keys.Order(StringComparer.Ordinal)));
    }
}
