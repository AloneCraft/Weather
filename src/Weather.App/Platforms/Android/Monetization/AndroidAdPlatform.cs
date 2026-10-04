using Google.Android.Gms.Ads;
using Weather.App.Services;
using Weather.Presentation.Monetization;
using Xamarin.Google.UserMesssagingPlatform;

namespace Weather.App.Monetization;

/// <summary>
/// 広告と同意(Android)。UMP で同意を確かめ(必要な地域だけフォームを出す)、広告を要求できるなら Google Mobile Ads を初期化する(Monetization.md「起動の流れ」)。
/// 位置情報は広告の要求に渡さない。
/// </summary>
internal sealed class AndroidAdPlatform:IAdPlatform{
    private bool initialized;

    public bool CanRequestAds=>this.initialized&&Information()?.CanRequestAds()==true;

    public bool IsPrivacyOptionsRequired=>Information()?.PrivacyOptionsRequirementStatus==ConsentInformationPrivacyOptionsRequirementStatus.Required;

    public Task GatherConsentAsync(CancellationToken cancellationToken){
        return MainThread.InvokeOnMainThreadAsync(this.GatherOnMainThreadAsync);
    }

    public Task ShowPrivacyOptionsAsync(){
        return MainThread.InvokeOnMainThreadAsync(static ()=>{
            var activity=Platform.CurrentActivity;
            if(activity is null){
                return Task.CompletedTask;
            }
            var done=new TaskCompletionSource();
            UserMessagingPlatform.ShowPrivacyOptionsForm(activity,new FormDismissed(_=>done.TrySetResult()));
            return done.Task;
        });
    }

    private async Task GatherOnMainThreadAsync(){
        var activity=Platform.CurrentActivity;
        if(activity is null){
            return;
        }
        var information=UserMessagingPlatform.GetConsentInformation(activity);
        var update=new TaskCompletionSource<bool>();
        information.RequestConsentInfoUpdate(activity,CreateParameters(activity),new UpdateSucceeded(()=>update.TrySetResult(true)),new UpdateFailed(error=>{
            System.Diagnostics.Debug.WriteLine($"同意の情報を更新できませんでした: {error?.Message}");
            update.TrySetResult(false);
        }));
        if(await update.Task){
            var dismissed=new TaskCompletionSource();
            UserMessagingPlatform.LoadAndShowConsentFormIfRequired(activity,new FormDismissed(error=>{
                if(error is not null){
                    System.Diagnostics.Debug.WriteLine($"同意フォームを表示できませんでした: {error.Message}");
                }
                dismissed.TrySetResult();
            }));
            await dismissed.Task;
        }
        //更新に失敗しても、前回の同意で広告を要求できるなら初期化する(UMP の推奨手順)
        if(information.CanRequestAds()&&!this.initialized){
            var completed=new TaskCompletionSource();
            MobileAds.Initialize(activity,new InitializationCompleted(()=>completed.TrySetResult()));
            await completed.Task;
            this.initialized=true;
        }
    }

    private static ConsentRequestParameters CreateParameters(Android.Content.Context context){
        var builder=new ConsentRequestParameters.Builder();
        var geography=AdUnits.DebugGeography;
        if(geography.Length>0){
            var debug=new ConsentDebugSettings.Builder(context);
            if(geography=="EEA"){
                debug.SetDebugGeography(ConsentDebugSettings.DebugGeography.DebugGeographyEea);
            }else{
                debug.SetDebugGeography(ConsentDebugSettings.DebugGeography.DebugGeographyOther);
            }
            if(AdUnits.TestDeviceHashedId.Length>0){
                debug.AddTestDeviceHashedId(AdUnits.TestDeviceHashedId);
            }
            builder.SetConsentDebugSettings(debug.Build());
        }
        return builder.Build();
    }

    private static IConsentInformation? Information(){
        var context=Platform.AppContext;
        if(context is null){
            return null;
        }
        return UserMessagingPlatform.GetConsentInformation(context);
    }

    private sealed class UpdateSucceeded(Action action):Java.Lang.Object,IConsentInformationOnConsentInfoUpdateSuccessListener{
        public void OnConsentInfoUpdateSuccess(){
            action();
        }
    }

    private sealed class UpdateFailed(Action<FormError?> action):Java.Lang.Object,IConsentInformationOnConsentInfoUpdateFailureListener{
        public void OnConsentInfoUpdateFailure(FormError? error){
            action(error);
        }
    }

    private sealed class FormDismissed(Action<FormError?> action):Java.Lang.Object,IConsentFormOnConsentFormDismissedListener{
        public void OnConsentFormDismissed(FormError? error){
            action(error);
        }
    }

    private sealed class InitializationCompleted(Action action):Java.Lang.Object,Google.Android.Gms.Ads.Initialization.IOnInitializationCompleteListener{
        public void OnInitializationComplete(Google.Android.Gms.Ads.Initialization.IInitializationStatus status){
            action();
        }
    }
}
