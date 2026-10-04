#if WEATHER_NATIVE_IOS
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;
using Weather.App.Controls;
using Weather.App.Services;
using Weather.Native.iOS;
using Weather.Presentation.Monetization;

namespace Weather.App.Monetization;

/// <summary>
/// 広告と同意(iOS)。Swift のラッパー(WNAds)で UMP の同意 → ATT → Google Mobile Ads の初期化を行う(Monetization.md「起動の流れ」)。
/// ラッパーは Mac でのビルド(-p:EnableNativeIos=true)でだけ含まれる。含まれないビルドでは何もしない実装を使う。
/// </summary>
internal sealed class IosAdPlatform:IAdPlatform{
    public bool CanRequestAds=>WNAds.CanRequestAds;

    public bool IsPrivacyOptionsRequired=>WNAds.IsPrivacyOptionsRequired;

    public Task GatherConsentAsync(CancellationToken cancellationToken){
        var done=new TaskCompletionSource();
        nint geography=0;
        if(AdUnits.DebugGeography=="EEA"){
            geography=1;
        }else if(AdUnits.DebugGeography.Length>0){
            geography=2;
        }
        WNAds.GatherConsent(geography,AdUnits.TestDeviceHashedId,()=>done.TrySetResult());
        return done.Task;
    }

    public Task ShowPrivacyOptionsAsync(){
        var done=new TaskCompletionSource();
        WNAds.ShowPrivacyOptions(()=>done.TrySetResult());
        return done.Task;
    }
}

/// <summary>「広告を消す」の課金(StoreKit 2。Swift のラッパー WNStore)。商品 ID は Android と同じ。</summary>
internal sealed class IosPurchasePlatform:IPurchasePlatform{
    public const string AdFreeProductId="remove_ads";

    public Task<StoreProduct?> GetAdFreeProductAsync(CancellationToken cancellationToken){
        var done=new TaskCompletionSource<StoreProduct?>();
        WNStore.DisplayPrice(AdFreeProductId,price=>{
            if(price is null){
                done.TrySetResult(null);
            }else{
                done.TrySetResult(new StoreProduct(AdFreeProductId,price.ToString()));
            }
        });
        return done.Task;
    }

    public Task<PurchaseOutcome> PurchaseAdFreeAsync(CancellationToken cancellationToken){
        var done=new TaskCompletionSource<PurchaseOutcome>();
        WNStore.Purchase(AdFreeProductId,code=>done.TrySetResult(Outcome(code)));
        return done.Task;
    }

    public Task<bool?> IsAdFreeOwnedAsync(CancellationToken cancellationToken){
        var done=new TaskCompletionSource<bool?>();
        WNStore.IsOwned(AdFreeProductId,code=>done.TrySetResult(Owned(code)));
        return done.Task;
    }

    public Task<bool?> RestoreAsync(CancellationToken cancellationToken){
        var done=new TaskCompletionSource<bool?>();
        WNStore.Restore(AdFreeProductId,code=>done.TrySetResult(Owned(code)));
        return done.Task;
    }

    /// <summary>WNStore の結果のコード(0 完了・1 取り消し・2 保留・3 失敗・4 利用できない)。</summary>
    internal static PurchaseOutcome Outcome(nint code){
        switch(code){
            case 0:
                return PurchaseOutcome.Purchased;
            case 1:
                return PurchaseOutcome.Cancelled;
            case 2:
                return PurchaseOutcome.Pending;
            case 4:
                return PurchaseOutcome.NotAvailable;
            default:
                return PurchaseOutcome.Failed;
        }
    }

    internal static bool? Owned(nint code){
        if(code==1){
            return true;
        }
        if(code==0){
            return false;
        }
        return null;
    }
}

/// <summary>iOS のバナー(GADBannerView を WNAds が作る)。画面の幅に合わせたアンカー型のアダプティブ バナーを 1 回だけ要求する。</summary>
internal sealed class NativeBannerHandler():ViewHandler<NativeBanner,UIView>(ViewMapper,ViewCommandMapper){
    protected override UIView CreatePlatformView(){
        var width=(double)UIScreen.MainScreen.Bounds.Width;
        var banner=WNAds.MakeBanner(AdUnits.Banner,width);
        this.VirtualView.Owner.OnBannerSized(WNAds.BannerHeight(width));
        return banner;
    }
}
#endif
