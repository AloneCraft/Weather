using Android.BillingClient.Api;
using Weather.Presentation.Monetization;

namespace Weather.App.Monetization;

/// <summary>
/// 「広告を消す」の課金(Google Play Billing)。非消耗型の 1 商品だけを扱う。購入は 3 日以内に承認しないと自動で返金されるため、
/// 所有の確認のたびに未承認の購入を承認する(Monetization.md)。
/// </summary>
internal sealed class AndroidPurchasePlatform:IPurchasePlatform{
    /// <summary>Play Console に登録する商品 ID(iOS の App Store Connect と同じ ID にする)。</summary>
    public const string AdFreeProductId="remove_ads";

    private readonly PurchasesListener listener=new();
    private BillingClient? client;

    public async Task<StoreProduct?> GetAdFreeProductAsync(CancellationToken cancellationToken){
        var details=await this.QueryProductAsync();
        var offer=details?.GetOneTimePurchaseOfferDetails();
        if(details is null||offer is null){
            return null;
        }
        return new StoreProduct(details.ProductId,offer.FormattedPrice);
    }

    public async Task<PurchaseOutcome> PurchaseAdFreeAsync(CancellationToken cancellationToken){
        var details=await this.QueryProductAsync();
        var activity=Platform.CurrentActivity;
        if(details is null||activity is null||this.client is null){
            return PurchaseOutcome.NotAvailable;
        }
        var product=BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(details).Build();
        var parameters=BillingFlowParams.NewBuilder().SetProductDetailsParamsList([product]).Build();
        var pending=this.listener.Begin();
        var launch=this.client.LaunchBillingFlow(activity,parameters);
        if(launch.ResponseCode!=BillingResponseCode.Ok){
            this.listener.Cancel();
            return Outcome(launch.ResponseCode,null);
        }
        var (result,purchases)=await pending;
        var outcome=Outcome(result.ResponseCode,purchases);
        if(outcome==PurchaseOutcome.Purchased){
            await this.AcknowledgeAsync(purchases!);
        }
        return outcome;
    }

    public async Task<bool?> IsAdFreeOwnedAsync(CancellationToken cancellationToken){
        var client=await this.ConnectAsync();
        if(client is null){
            return null;
        }
        var query=await client.QueryPurchasesAsync(QueryPurchasesParams.NewBuilder().SetProductType(BillingClient.ProductType.Inapp).Build());
        if(query.Result.ResponseCode!=BillingResponseCode.Ok){
            return null;
        }
        var owned=query.Purchases.Where(static p=>p.Products.Contains(AdFreeProductId)&&p.PurchaseState==PurchaseState.Purchased).ToList();
        await this.AcknowledgeAsync(owned);
        return owned.Count>0;
    }

    /// <summary>Play は購入をアカウントに紐づけて返すため、復元は所有の確認と同じ。</summary>
    public Task<bool?> RestoreAsync(CancellationToken cancellationToken){
        return this.IsAdFreeOwnedAsync(cancellationToken);
    }

    private async Task<BillingClient?> ConnectAsync(){
        this.client??=BillingClient.NewBuilder(Platform.AppContext)
            .SetListener(this.listener)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
            .EnableAutoServiceReconnection()
            .Build();
        if(this.client.IsReady){
            return this.client;
        }
        var result=await this.client.StartConnectionAsync();
        if(result.ResponseCode!=BillingResponseCode.Ok){
            System.Diagnostics.Debug.WriteLine($"Play Billing に接続できませんでした: {result.ResponseCode} {result.DebugMessage}");
            return null;
        }
        return this.client;
    }

    private async Task<ProductDetails?> QueryProductAsync(){
        var client=await this.ConnectAsync();
        if(client is null){
            return null;
        }
        var product=QueryProductDetailsParams.Product.NewBuilder().SetProductId(AdFreeProductId).SetProductType(BillingClient.ProductType.Inapp).Build();
        var query=await client.QueryProductDetailsAsync(QueryProductDetailsParams.NewBuilder().SetProductList([product]).Build());
        if(query.Result.ResponseCode!=BillingResponseCode.Ok){
            return null;
        }
        return query.ProductDetails?.FirstOrDefault();
    }

    private async Task AcknowledgeAsync(IEnumerable<Purchase> purchases){
        if(this.client is null){
            return;
        }
        foreach(var purchase in purchases){
            if(purchase.PurchaseState!=PurchaseState.Purchased||purchase.IsAcknowledged){
                continue;
            }
            var result=await this.client.AcknowledgePurchaseAsync(AcknowledgePurchaseParams.NewBuilder().SetPurchaseToken(purchase.PurchaseToken).Build());
            if(result.ResponseCode!=BillingResponseCode.Ok){
                System.Diagnostics.Debug.WriteLine($"購入を承認できませんでした(次回の確認で再試行): {result.ResponseCode}");
            }
        }
    }

    private static PurchaseOutcome Outcome(BillingResponseCode code,IList<Purchase>? purchases){
        if(code==BillingResponseCode.ItemAlreadyOwned){
            return PurchaseOutcome.Purchased;
        }
        if(code==BillingResponseCode.Ok){
            var purchase=purchases?.FirstOrDefault(static p=>p.Products.Contains(AdFreeProductId));
            if(purchase is null){
                return PurchaseOutcome.Failed;
            }
            if(purchase.PurchaseState==PurchaseState.Pending){
                return PurchaseOutcome.Pending;
            }
            return PurchaseOutcome.Purchased;
        }
        if(code==BillingResponseCode.UserCancelled){
            return PurchaseOutcome.Cancelled;
        }
        if(code==BillingResponseCode.BillingUnavailable||code==BillingResponseCode.ServiceUnavailable||code==BillingResponseCode.ServiceDisconnected||code==BillingResponseCode.NetworkError){
            return PurchaseOutcome.NotAvailable;
        }
        return PurchaseOutcome.Failed;
    }

    /// <summary>購入の結果は BillingClient の listener に届く。購入画面 1 回分を Task にする。</summary>
    private sealed class PurchasesListener:Java.Lang.Object,IPurchasesUpdatedListener{
        private TaskCompletionSource<(BillingResult,IList<Purchase>?)>? pending;

        public Task<(BillingResult,IList<Purchase>?)> Begin(){
            this.pending=new TaskCompletionSource<(BillingResult,IList<Purchase>?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            return this.pending.Task;
        }

        public void Cancel(){
            this.pending=null;
        }

        public void OnPurchasesUpdated(BillingResult result,IList<Purchase>? purchases){
            //購入画面の外(保留の支払いの完了など)で届いた結果は、次回の所有の確認で反映する
            this.pending?.TrySetResult((result,purchases));
            this.pending=null;
        }
    }
}
