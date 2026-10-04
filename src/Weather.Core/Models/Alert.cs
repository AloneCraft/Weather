namespace Weather.Core;

public enum AlertTier{Information,Advisory,Watch,Warning,Danger,Emergency}
public enum AlertStatus{Active,Updated,Downgraded,Cancelled}

/// <summary>警報・注意報。名称と本文は機関の表現をそのまま保持する。</summary>
public sealed record Alert{
    public Alert(string id,string eventName,AlertTier tier,AlertStatus status,SourceAttribution source){
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(source);
        this.Id=id;
        this.EventName=eventName;
        this.Tier=tier;
        this.Status=status;
        this.Source=source;
    }

    public string Id{get;}
    public string EventName{get;}
    public AlertTier Tier{get;}
    public AlertStatus Status{get;}
    public SourceAttribution Source{get;}
    public string? EventCode{get;init;}

    /// <summary>気象庁の警戒レベル相当(2〜5)。大雨・土砂災害・氾濫・高潮のみ。</summary>
    public int? WarningLevel{
        get;
        init{
            if(value is <2 or >5){
                throw new ArgumentOutOfRangeException(nameof(this.WarningLevel),value,"2〜5 の範囲外です。");
            }
            field=value;
        }
    }

    public DateTimeOffset? Onset{get;init;}
    public DateTimeOffset? Expires{get;init;}
    public string? AreaName{get;init;}
    public string? Headline{get;init;}
    public string? Description{get;init;}
    public string? Instruction{get;init;}
    public string? SourceSeverity{get;init;}
}

/// <summary>地点の警報の一覧。空の一覧は「警報なし」を表す(非対応とは区別する)。</summary>
public sealed class AlertSet{
    public AlertSet(IReadOnlyList<Alert> alerts,SourceAttribution source,string? headline=null){
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(source);
        this.Alerts=[..alerts];
        this.Source=source;
        this.Headline=headline;
    }

    public IReadOnlyList<Alert> Alerts{get;}
    public SourceAttribution Source{get;}
    public string? Headline{get;}

    public IEnumerable<Alert> Active=>this.Alerts.Where(static a=>a.Status!=AlertStatus.Cancelled);
}
