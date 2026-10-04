using CommunityToolkit.Mvvm.ComponentModel;

namespace Weather.Presentation.Monetization;

/// <summary>
/// バナーを出すかの判断(Monetization.md)。購入済みなら出さず、同意の確認が済んで広告を要求できるときだけ出す。
/// 購入済みかは設定にキャッシュし、起動直後・オフラインでも購入済みなら広告を出さない。起動時にストアで確かめ直す(返金・取消を反映する)。
/// </summary>
public sealed partial class AdPolicy:ObservableObject{
    private readonly IAdPlatform ads;
    private readonly IPurchasePlatform purchases;
    private readonly AppSettings settings;
    private Task? start;

    public AdPolicy(IAdPlatform ads,IPurchasePlatform purchases,AppSettings settings){
        this.ads=ads;
        this.purchases=purchases;
        this.settings=settings;
        this.Refresh();
    }

    /// <summary>バナーを置いた画面(地図・地点の詳細)がこれを見て表示し、広告を要求する。</summary>
    [ObservableProperty]public partial bool ShowBanner{get;private set;}

    [ObservableProperty]public partial bool IsAdFree{get;private set;}

    /// <summary>設定画面に「広告のプライバシー設定」を出すか。広告を出さないなら不要。</summary>
    public bool IsPrivacyOptionsRequired=>!this.IsAdFree&&this.ads.IsPrivacyOptionsRequired;

    /// <summary>起動時に 1 回だけ実行する(2 回目以降は最初の実行を返す)。地図の表示後に呼ぶ(同意フォームが地図の上に出る)。</summary>
    public Task StartAsync(CancellationToken cancellationToken){
        return this.start??=this.StartCoreAsync(cancellationToken);
    }

    public async Task<PurchaseOutcome> PurchaseAdFreeAsync(CancellationToken cancellationToken){
        var outcome=await this.purchases.PurchaseAdFreeAsync(cancellationToken);
        if(outcome==PurchaseOutcome.Purchased){
            this.settings.AdFree=true;
            this.Refresh();
        }
        return outcome;
    }

    public async Task<bool?> RestoreAsync(CancellationToken cancellationToken){
        var owned=await this.purchases.RestoreAsync(cancellationToken);
        await this.ApplyOwnershipAsync(owned,cancellationToken);
        return owned;
    }

    public Task ShowPrivacyOptionsAsync(){
        return this.ads.ShowPrivacyOptionsAsync();
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken){
        var owned=await this.purchases.IsAdFreeOwnedAsync(cancellationToken);
        await this.ApplyOwnershipAsync(owned,cancellationToken);
    }

    /// <summary>ストアの結果を反映する。問い合わせられなかった(null)ならキャッシュを維持する。広告を出すなら同意を確かめる。</summary>
    private async Task ApplyOwnershipAsync(bool? owned,CancellationToken cancellationToken){
        if(owned is bool value){
            this.settings.AdFree=value;
        }
        this.Refresh();
        if(!this.settings.AdFree&&!this.ads.CanRequestAds){
            await this.ads.GatherConsentAsync(cancellationToken);
            this.Refresh();
        }
    }

    private void Refresh(){
        this.IsAdFree=this.settings.AdFree;
        this.ShowBanner=!this.IsAdFree&&this.ads.CanRequestAds;
        this.OnPropertyChanged(nameof(this.IsPrivacyOptionsRequired));
    }
}
