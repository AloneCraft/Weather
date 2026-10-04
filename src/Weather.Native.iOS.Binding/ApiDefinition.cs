using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Weather.Native.iOS;

/// <summary>WNAds(Swift)。広告と同意。</summary>
[BaseType(typeof(NSObject),Name="WNAds")]
interface WNAds{
    [Static,Export("canRequestAds")]
    bool CanRequestAds{get;}

    [Static,Export("isPrivacyOptionsRequired")]
    bool IsPrivacyOptionsRequired{get;}

    [Static,Export("gatherConsentWithDebugGeography:testDeviceId:completion:")]
    void GatherConsent(nint debugGeography,string testDeviceId,Action completion);

    [Static,Export("showPrivacyOptionsWithCompletion:")]
    void ShowPrivacyOptions(Action completion);

    [Static,Export("bannerHeightWithWidth:")]
    double BannerHeight(double width);

    [Static,Export("makeBannerWithAdUnitId:width:")]
    UIView MakeBanner(string adUnitId,double width);
}

/// <summary>WNStore(Swift)。StoreKit 2 の課金。</summary>
[BaseType(typeof(NSObject),Name="WNStore")]
interface WNStore{
    [Static,Export("displayPriceWithProductId:completion:")]
    void DisplayPrice(string productId,Action<NSString?> completion);

    [Static,Export("purchaseWithProductId:completion:")]
    void Purchase(string productId,Action<nint> completion);

    [Static,Export("isOwnedWithProductId:completion:")]
    void IsOwned(string productId,Action<nint> completion);

    [Static,Export("restoreWithProductId:completion:")]
    void Restore(string productId,Action<nint> completion);
}
