using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Presentation.Monetization;
using Weather.Presentation.Resources;

namespace Weather.Presentation.ViewModels;

/// <summary>設定画面の「広告」の欄(Monetization.md): 広告を消す(購入)・購入の復元・広告のプライバシー設定。</summary>
public sealed partial class AdSettingsViewModel:ObservableObject{
    private readonly AdPolicy policy;
    private readonly IPurchasePlatform purchases;
    private readonly IDialogService dialogs;
    private StoreProduct? product;

    public AdSettingsViewModel(AdPolicy policy,IPurchasePlatform purchases,IDialogService dialogs){
        this.policy=policy;
        this.purchases=purchases;
        this.dialogs=dialogs;
        this.policy.PropertyChanged+=this.OnPolicyChanged;
    }

    public bool IsAdFree=>this.policy.IsAdFree;

    public bool IsPrivacyOptionsRequired=>this.policy.IsPrivacyOptionsRequired;

    /// <summary>商品を取得できたときだけ購入できる(価格を表示できないまま購入させない)。</summary>
    public bool CanPurchase=>this.product is not null&&!this.policy.IsAdFree;

    public string PurchaseText{
        get{
            if(this.product is null){
                return Strings.RemoveAds;
            }
            return string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.RemoveAdsWithPrice,this.product.DisplayPrice);
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken){
        if(this.policy.IsAdFree){
            return;
        }
        this.product=await this.purchases.GetAdFreeProductAsync(cancellationToken);
        this.OnPropertyChanged(nameof(this.CanPurchase));
        this.OnPropertyChanged(nameof(this.PurchaseText));
    }

    [RelayCommand]
    private async Task PurchaseAsync(CancellationToken cancellationToken){
        var outcome=await this.policy.PurchaseAdFreeAsync(cancellationToken);
        if(outcome==PurchaseOutcome.Cancelled){
            return;
        }
        await this.dialogs.AlertAsync(Strings.RemoveAds,PurchaseMessage(outcome));
    }

    [RelayCommand]
    private async Task RestoreAsync(CancellationToken cancellationToken){
        var owned=await this.policy.RestoreAsync(cancellationToken);
        string message;
        if(owned is null){
            message=Strings.StoreUnavailable;
        }else if(owned.Value){
            message=Strings.RestoreCompleted;
        }else{
            message=Strings.RestoreNotFound;
        }
        await this.dialogs.AlertAsync(Strings.RestorePurchases,message);
    }

    [RelayCommand]
    private Task OpenPrivacyOptionsAsync(){
        return this.policy.ShowPrivacyOptionsAsync();
    }

    private static string PurchaseMessage(PurchaseOutcome outcome){
        if(outcome==PurchaseOutcome.Purchased){
            return Strings.PurchaseCompleted;
        }
        if(outcome==PurchaseOutcome.Pending){
            return Strings.PurchasePending;
        }
        if(outcome==PurchaseOutcome.NotAvailable){
            return Strings.StoreUnavailable;
        }
        return Strings.PurchaseFailed;
    }

    private void OnPolicyChanged(object? sender,PropertyChangedEventArgs e){
        if(e.PropertyName==nameof(AdPolicy.IsAdFree)){
            this.OnPropertyChanged(nameof(this.IsAdFree));
            this.OnPropertyChanged(nameof(this.CanPurchase));
        }else if(e.PropertyName==nameof(AdPolicy.IsPrivacyOptionsRequired)){
            this.OnPropertyChanged(nameof(this.IsPrivacyOptionsRequired));
        }
    }
}
