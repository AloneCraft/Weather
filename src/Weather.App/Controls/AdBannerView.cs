using System.ComponentModel;
using Weather.Presentation.Monetization;
using Weather.Presentation.Resources;

namespace Weather.App.Controls;

/// <summary>
/// 広告のバナー(Monetization.md)。置くのは地図と地点の詳細の最下部の行だけ(地図・出典の帯に重ねない)。
/// AdPolicy.ShowBanner が true の間だけ表示して広告を要求し、false になったら(購入・同意なし)OS のバナーを破棄して高さ 0 にする。
/// </summary>
public sealed class AdBannerView:ContentView{
    private AdPolicy? policy;

    public AdBannerView(){
        this.IsVisible=false;
        this.HeightRequest=0;
        SemanticProperties.SetDescription(this,Strings.AdBannerDescription);
    }

    /// <summary>OS のバナーの高さ(dp)が決まったときに呼ぶ(アダプティブ バナーは画面の幅で高さが変わる)。</summary>
    internal void OnBannerSized(double height){
        this.HeightRequest=height;
    }

    protected override void OnHandlerChanged(){
        base.OnHandlerChanged();
        if(this.policy is not null){
            this.policy.PropertyChanged-=this.OnPolicyChanged;
            this.policy=null;
        }
        var services=this.Handler?.MauiContext?.Services;
        if(services is null){
            this.Content=null;
            return;
        }
        this.policy=services.GetRequiredService<AdPolicy>();
        this.policy.PropertyChanged+=this.OnPolicyChanged;
        this.Update();
    }

    private void OnPolicyChanged(object? sender,PropertyChangedEventArgs e){
        if(e.PropertyName==nameof(AdPolicy.ShowBanner)){
            MainThread.BeginInvokeOnMainThread(this.Update);
        }
    }

    private void Update(){
        var show=this.policy?.ShowBanner==true;
        if(show&&this.Content is null){
            this.Content=new NativeBanner(this);
        }else if(!show&&this.Content is not null){
            this.Content=null;
            this.HeightRequest=0;
        }
        this.IsVisible=show;
    }
}

/// <summary>OS の広告ビュー(Android は AdView、iOS は Swift のラッパーの GADBannerView)。生成した時点で広告を要求する。</summary>
internal sealed class NativeBanner(AdBannerView owner):View{
    public AdBannerView Owner{get;}=owner;
}
