namespace Weather.Presentation.Monetization;

/// <summary>広告と同意(Monetization.md)。Android は Google Mobile Ads + UMP、iOS は Swift のラッパー経由で同じものを使う。</summary>
public interface IAdPlatform{
    /// <summary>
    /// 同意の情報を更新し、必要なら同意フォーム(EEA・英国等)と iOS の ATT を表示してから、広告を要求できるなら SDK を初期化する。
    /// 失敗しても例外にせず、広告を要求できない状態(<see cref="CanRequestAds"/> が false)のままにする。
    /// </summary>
    Task GatherConsentAsync(CancellationToken cancellationToken);

    /// <summary>同意の状態から広告を要求してよいか(UMP の canRequestAds)。同意の確認前は false。</summary>
    bool CanRequestAds{get;}

    /// <summary>プライバシー設定(同意の変更)の入口を設定画面に出す必要があるか(UMP の privacyOptionsRequirementStatus)。</summary>
    bool IsPrivacyOptionsRequired{get;}

    Task ShowPrivacyOptionsAsync();
}

public enum PurchaseOutcome{Purchased,Cancelled,Pending,Failed,NotAvailable}

/// <summary>ストアの商品。価格はストアが地域に合わせて書式化した文字列をそのまま表示する。</summary>
public sealed record StoreProduct(string Id,string DisplayPrice);

/// <summary>「広告を消す」(買い切り・非消耗型)の課金。Android は Play Billing、iOS は StoreKit 2。</summary>
public interface IPurchasePlatform{
    Task<StoreProduct?> GetAdFreeProductAsync(CancellationToken cancellationToken);

    Task<PurchaseOutcome> PurchaseAdFreeAsync(CancellationToken cancellationToken);

    /// <summary>所有しているか。ストアに問い合わせられなかったときは null(キャッシュの値を維持する)。</summary>
    Task<bool?> IsAdFreeOwnedAsync(CancellationToken cancellationToken);

    /// <summary>購入の復元(iOS は AppStore.sync)。結果は <see cref="IsAdFreeOwnedAsync"/> と同じ。</summary>
    Task<bool?> RestoreAsync(CancellationToken cancellationToken);
}

/// <summary>広告と課金に対応しない環境(テスト・未対応 OS)。広告は出さず、購入もできない。</summary>
public sealed class NullMonetizationPlatform:IAdPlatform,IPurchasePlatform{
    public bool CanRequestAds=>false;

    public bool IsPrivacyOptionsRequired=>false;

    public Task GatherConsentAsync(CancellationToken cancellationToken){
        return Task.CompletedTask;
    }

    public Task ShowPrivacyOptionsAsync(){
        return Task.CompletedTask;
    }

    public Task<StoreProduct?> GetAdFreeProductAsync(CancellationToken cancellationToken){
        return Task.FromResult<StoreProduct?>(null);
    }

    public Task<PurchaseOutcome> PurchaseAdFreeAsync(CancellationToken cancellationToken){
        return Task.FromResult(PurchaseOutcome.NotAvailable);
    }

    public Task<bool?> IsAdFreeOwnedAsync(CancellationToken cancellationToken){
        return Task.FromResult<bool?>(null);
    }

    public Task<bool?> RestoreAsync(CancellationToken cancellationToken){
        return Task.FromResult<bool?>(null);
    }
}
