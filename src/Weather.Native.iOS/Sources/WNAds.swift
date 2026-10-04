import Foundation
import UIKit
import AppTrackingTransparency
import GoogleMobileAds
import UserMessagingPlatform

/// 広告と同意(Monetization.md「起動の流れ」)。C# からは Objective-C の名前で呼ぶ(Weather.Native.iOS.Binding)。
/// 位置情報は広告の要求に渡さない。
@objc(WNAds)
public final class WNAds: NSObject {
    private static var initialized = false

    /// 同意の確認が済み、SDK を初期化して広告を要求できるか。
    @objc public static var canRequestAds: Bool {
        return initialized && ConsentInformation.shared.canRequestAds
    }

    @objc public static var isPrivacyOptionsRequired: Bool {
        return ConsentInformation.shared.privacyOptionsRequirementStatus == .required
    }

    /// 同意の情報を更新し、必要な地域だけ同意フォームを出し、iOS の ATT を求めてから、広告を要求できるなら SDK を初期化する。
    /// debugGeography: 0 = 固定しない、1 = EEA、2 = それ以外(Debug ビルドの試験用)。
    @objc public static func gatherConsent(debugGeography: Int, testDeviceId: String, completion: @escaping () -> Void) {
        DispatchQueue.main.async {
            let parameters = RequestParameters()
            if debugGeography != 0 {
                let debug = DebugSettings()
                debug.geography = debugGeography == 1 ? .EEA : .other
                if !testDeviceId.isEmpty {
                    debug.testDeviceIdentifiers = [testDeviceId]
                }
                parameters.debugSettings = debug
            }
            ConsentInformation.shared.requestConsentInfoUpdate(with: parameters) { updateError in
                let afterForm = {
                    requestTracking {
                        startIfAllowed(completion: completion)
                    }
                }
                if updateError != nil {
                    // 更新に失敗しても、前回の同意で広告を要求できるなら初期化する(UMP の推奨手順)
                    afterForm()
                    return
                }
                guard let root = topViewController() else {
                    afterForm()
                    return
                }
                ConsentForm.loadAndPresentIfRequired(from: root) { _ in
                    afterForm()
                }
            }
        }
    }

    @objc public static func showPrivacyOptions(completion: @escaping () -> Void) {
        DispatchQueue.main.async {
            guard let root = topViewController() else {
                completion()
                return
            }
            ConsentForm.presentPrivacyOptionsForm(from: root) { _ in
                completion()
            }
        }
    }

    /// 画面の幅(pt)に合わせたアンカー型のアダプティブ バナーの高さ(pt)。
    @objc public static func bannerHeight(width: Double) -> Double {
        return Double(currentOrientationAnchoredAdaptiveBanner(width: CGFloat(width)).size.height)
    }

    /// バナーを作り、1 回だけ広告を要求する。表示中の画面の最前面の ViewController を rootViewController にする。
    @objc public static func makeBanner(adUnitId: String, width: Double) -> UIView {
        let banner = BannerView(adSize: currentOrientationAnchoredAdaptiveBanner(width: CGFloat(width)))
        banner.adUnitID = adUnitId
        banner.rootViewController = topViewController()
        banner.load(Request())
        return banner
    }

    private static func requestTracking(_ next: @escaping () -> Void) {
        // 同意フォーム(IDFA の説明を含む)の後に ATT を求める。未決定のときだけ表示される
        if #available(iOS 14, *), ATTrackingManager.trackingAuthorizationStatus == .notDetermined {
            ATTrackingManager.requestTrackingAuthorization { _ in
                DispatchQueue.main.async { next() }
            }
            return
        }
        next()
    }

    private static func startIfAllowed(completion: @escaping () -> Void) {
        guard ConsentInformation.shared.canRequestAds, !initialized else {
            completion()
            return
        }
        MobileAds.shared.start { _ in
            DispatchQueue.main.async {
                initialized = true
                completion()
            }
        }
    }

    static func topViewController() -> UIViewController? {
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        let window = scenes.flatMap { $0.windows }.first { $0.isKeyWindow } ?? UIApplication.shared.windows.first
        var top = window?.rootViewController
        while let presented = top?.presentedViewController {
            top = presented
        }
        return top
    }
}
