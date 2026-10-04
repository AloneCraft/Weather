using Weather.Presentation;
using Weather.Presentation.Monetization;
using Settings=Weather.Presentation.AppSettings;
using Policy=Weather.Presentation.Monetization.AdPolicy;
using SettingsVm=Weather.Presentation.ViewModels.AdSettingsViewModel;

namespace Weather.Presentation.Tests;

/// <summary>広告と同意(UMP・ATT)。GatherConsentAsync を呼ぶと Consent の値が CanRequestAds になる。</summary>
internal sealed class FakeAdPlatform:IAdPlatform{
    public bool Consent{get;set;}=true;
    public bool CanRequestAds{get;private set;}
    public bool IsPrivacyOptionsRequired{get;set;}
    public int GatherCalls{get;private set;}
    public int PrivacyOptionsCalls{get;private set;}

    public Task GatherConsentAsync(CancellationToken cancellationToken){
        this.GatherCalls++;
        this.CanRequestAds=this.Consent;
        return Task.CompletedTask;
    }

    public Task ShowPrivacyOptionsAsync(){
        this.PrivacyOptionsCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class FakePurchasePlatform:IPurchasePlatform{
    public StoreProduct? Product{get;set;}=new("remove_ads","¥300");
    public PurchaseOutcome Outcome{get;set;}=PurchaseOutcome.Purchased;
    /// <summary>ストアの所有の結果。null はストアに問い合わせられない。</summary>
    public bool? Owned{get;set;}=false;

    public Task<StoreProduct?> GetAdFreeProductAsync(CancellationToken cancellationToken){
        return Task.FromResult(this.Product);
    }

    public Task<PurchaseOutcome> PurchaseAdFreeAsync(CancellationToken cancellationToken){
        return Task.FromResult(this.Outcome);
    }

    public Task<bool?> IsAdFreeOwnedAsync(CancellationToken cancellationToken){
        return Task.FromResult(this.Owned);
    }

    public Task<bool?> RestoreAsync(CancellationToken cancellationToken){
        return Task.FromResult(this.Owned);
    }
}

public class AdPolicy{
    private static (Policy Policy,FakeAdPlatform Ads,FakePurchasePlatform Purchases,Settings Settings) Create(bool cachedAdFree=false){
        var settings=new Settings(new FakeSettingsStore());
        settings.AdFree=cachedAdFree;
        var ads=new FakeAdPlatform();
        var purchases=new FakePurchasePlatform();
        return (new Policy(ads,purchases,settings),ads,purchases,settings);
    }

    [Fact,Trait("Category","Unit")]public async Task ShowBanner(){
        {
            //同意の確認前は出さない(広告を要求しない)
            var (policy,_,_,_)=Create();
            Assert.False(policy.ShowBanner);
        }
        {
            //未購入で同意が取れれば出す
            var (policy,ads,_,_)=Create();
            await policy.StartAsync(CancellationToken.None);
            Assert.Equal(1,ads.GatherCalls);
            Assert.True(policy.ShowBanner);
        }
        {
            //同意フォームで広告を要求できない状態なら出さない
            var (policy,ads,_,_)=Create();
            ads.Consent=false;
            await policy.StartAsync(CancellationToken.None);
            Assert.False(policy.ShowBanner);
        }
        {
            //購入済みなら出さず、同意の確認(フォーム・ATT)もしない
            var (policy,ads,purchases,_)=Create();
            purchases.Owned=true;
            await policy.StartAsync(CancellationToken.None);
            Assert.False(policy.ShowBanner);
            Assert.Equal(0,ads.GatherCalls);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task IsAdFree(){
        {
            //起動直後はキャッシュで判断する(ストアの確認前・オフラインでも購入済みなら広告を出さない)
            var (policy,_,purchases,_)=Create(cachedAdFree:true);
            Assert.True(policy.IsAdFree);
            purchases.Owned=null;
            await policy.StartAsync(CancellationToken.None);
            Assert.True(policy.IsAdFree);
            Assert.False(policy.ShowBanner);
        }
        {
            //ストアで所有していない(返金・取消)と分かればキャッシュを消して広告を出す
            var (policy,_,purchases,settings)=Create(cachedAdFree:true);
            purchases.Owned=false;
            await policy.StartAsync(CancellationToken.None);
            Assert.False(policy.IsAdFree);
            Assert.False(settings.AdFree);
            Assert.True(policy.ShowBanner);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task IsPrivacyOptionsRequired(){
        {
            //広告を出すなら UMP の要否に従う
            var (policy,ads,_,_)=Create();
            ads.IsPrivacyOptionsRequired=true;
            await policy.StartAsync(CancellationToken.None);
            Assert.True(policy.IsPrivacyOptionsRequired);
        }
        {
            //購入済みなら不要
            var (policy,ads,purchases,_)=Create();
            ads.IsPrivacyOptionsRequired=true;
            purchases.Owned=true;
            await policy.StartAsync(CancellationToken.None);
            Assert.False(policy.IsPrivacyOptionsRequired);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task StartAsync(){
        {
            //2 回呼んでも同意の確認は 1 回
            var (policy,ads,_,_)=Create();
            await policy.StartAsync(CancellationToken.None);
            await policy.StartAsync(CancellationToken.None);
            Assert.Equal(1,ads.GatherCalls);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task PurchaseAdFreeAsync(){
        {
            //購入するとバナーが消え、キャッシュに残る
            var (policy,_,_,settings)=Create();
            await policy.StartAsync(CancellationToken.None);
            Assert.Equal(PurchaseOutcome.Purchased,await policy.PurchaseAdFreeAsync(CancellationToken.None));
            Assert.False(policy.ShowBanner);
            Assert.True(settings.AdFree);
        }
        {
            //保留(支払い待ち)・取り消しでは広告を消さない
            foreach(var outcome in new[]{PurchaseOutcome.Pending,PurchaseOutcome.Cancelled,PurchaseOutcome.Failed}){
                var (policy,_,purchases,settings)=Create();
                purchases.Outcome=outcome;
                await policy.StartAsync(CancellationToken.None);
                await policy.PurchaseAdFreeAsync(CancellationToken.None);
                Assert.True(policy.ShowBanner);
                Assert.False(settings.AdFree);
            }
        }
    }

    [Fact,Trait("Category","Unit")]public async Task RestoreAsync(){
        {
            //復元で所有が見つかればバナーが消える
            var (policy,_,purchases,_)=Create();
            await policy.StartAsync(CancellationToken.None);
            purchases.Owned=true;
            Assert.True(await policy.RestoreAsync(CancellationToken.None));
            Assert.False(policy.ShowBanner);
        }
        {
            //ストアに接続できなければ状態を変えない
            var (policy,_,purchases,_)=Create(cachedAdFree:true);
            purchases.Owned=null;
            Assert.Null(await policy.RestoreAsync(CancellationToken.None));
            Assert.True(policy.IsAdFree);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task ShowPrivacyOptionsAsync(){
        var (policy,ads,_,_)=Create();
        await policy.ShowPrivacyOptionsAsync();
        Assert.Equal(1,ads.PrivacyOptionsCalls);
    }
}

public class AdSettingsViewModel{
    private static (SettingsVm Vm,Policy Policy,FakePurchasePlatform Purchases,FakeDialogs Dialogs) Create(){
        var settings=new Settings(new FakeSettingsStore());
        var purchases=new FakePurchasePlatform();
        var policy=new Policy(new FakeAdPlatform(),purchases,settings);
        var dialogs=new FakeDialogs();
        return (new SettingsVm(policy,purchases,dialogs),policy,purchases,dialogs);
    }

    [Fact,Trait("Category","Unit")]public async Task CanPurchase(){
        {
            //商品(価格)を取得できたら購入でき、価格を表示する
            var (vm,_,_,_)=Create();
            Assert.False(vm.CanPurchase);
            await vm.LoadAsync(CancellationToken.None);
            Assert.True(vm.CanPurchase);
            Assert.Equal("広告を消す(¥300)",vm.PurchaseText);
        }
        {
            //商品を取得できなければ購入させない
            var (vm,_,purchases,_)=Create();
            purchases.Product=null;
            await vm.LoadAsync(CancellationToken.None);
            Assert.False(vm.CanPurchase);
            Assert.Equal("広告を消す",vm.PurchaseText);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task PurchaseCommand(){
        {
            //購入すると購入済みの表示になる
            var (vm,_,_,dialogs)=Create();
            await vm.LoadAsync(CancellationToken.None);
            var changed=new List<string?>();
            vm.PropertyChanged+=(_,e)=>changed.Add(e.PropertyName);
            await vm.PurchaseCommand.ExecuteAsync(null);
            Assert.True(vm.IsAdFree);
            Assert.False(vm.CanPurchase);
            Assert.Contains(nameof(vm.IsAdFree),changed);
            Assert.Equal(["ご購入ありがとうございます。広告を表示しなくなりました。"],dialogs.Messages);
        }
        {
            //利用者が取り消したら何も表示しない
            var (vm,_,purchases,dialogs)=Create();
            purchases.Outcome=PurchaseOutcome.Cancelled;
            await vm.PurchaseCommand.ExecuteAsync(null);
            Assert.Empty(dialogs.Messages);
        }
        {
            //支払い待ちは完了後に消えることを伝える
            var (vm,_,purchases,dialogs)=Create();
            purchases.Outcome=PurchaseOutcome.Pending;
            await vm.PurchaseCommand.ExecuteAsync(null);
            Assert.False(vm.IsAdFree);
            Assert.Equal(["支払いが完了すると広告が消えます。"],dialogs.Messages);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task RestoreCommand(){
        {
            //見つからない
            var (vm,_,_,dialogs)=Create();
            await vm.RestoreCommand.ExecuteAsync(null);
            Assert.Equal(["このアカウントでの購入は見つかりませんでした。"],dialogs.Messages);
        }
        {
            //ストアに接続できない
            var (vm,_,purchases,dialogs)=Create();
            purchases.Owned=null;
            await vm.RestoreCommand.ExecuteAsync(null);
            Assert.Equal(["ストアに接続できませんでした。"],dialogs.Messages);
        }
        {
            //見つかった
            var (vm,_,purchases,dialogs)=Create();
            purchases.Owned=true;
            await vm.RestoreCommand.ExecuteAsync(null);
            Assert.True(vm.IsAdFree);
            Assert.Equal(["購入を復元しました。広告を表示しなくなりました。"],dialogs.Messages);
        }
    }
}
