using Android.Widget;
using Google.Android.Gms.Ads;
using Microsoft.Maui.Handlers;
using Weather.App.Controls;
using Weather.App.Services;

namespace Weather.App.Monetization;

/// <summary>Android のバナー(AdView)。画面の幅に合わせたアンカー型のアダプティブ バナー(大)を 1 回だけ要求し、外したら破棄する。</summary>
internal sealed class NativeBannerHandler():ViewHandler<NativeBanner,FrameLayout>(ViewMapper,ViewCommandMapper){
    private AdView? adView;

    protected override FrameLayout CreatePlatformView(){
        return new FrameLayout(this.Context);
    }

    protected override void ConnectHandler(FrameLayout platformView){
        base.ConnectHandler(platformView);
        var display=DeviceDisplay.Current.MainDisplayInfo;
        var widthDp=(int)(display.Width/display.Density);
        var size=AdSize.GetLargeAnchoredAdaptiveBannerAdSize(this.Context,widthDp);
        this.adView=new AdView(this.Context){AdUnitId=AdUnits.Banner,AdSize=size};
        platformView.AddView(this.adView);
        this.VirtualView.Owner.OnBannerSized(size.Height);
        this.adView.LoadAd(new AdRequest.Builder().Build());
    }

    protected override void DisconnectHandler(FrameLayout platformView){
        if(this.adView is not null){
            platformView.RemoveView(this.adView);
            this.adView.Destroy();
            this.adView=null;
        }
        base.DisconnectHandler(platformView);
    }
}
