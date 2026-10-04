import Foundation
import StoreKit

/// 「広告を消す」(非消耗型)の課金。StoreKit 2(StoreKit 1 は非推奨)。C# からは完了ハンドラーで呼ぶ。
/// 結果のコード: 購入 0 = 完了、1 = 取り消し、2 = 保留、3 = 失敗、4 = 利用できない。所有 1 = あり、0 = なし、-1 = 不明。
@objc(WNStore)
public final class WNStore: NSObject {
    /// 商品の価格(地域に合わせて書式化された文字列)。取得できなければ nil。
    @objc public static func displayPrice(productId: String, completion: @escaping (String?) -> Void) {
        Task {
            let product = try? await Product.products(for: [productId]).first
            await MainActor.run { completion(product?.displayPrice) }
        }
    }

    @objc public static func purchase(productId: String, completion: @escaping (Int) -> Void) {
        Task {
            let code = await purchaseCode(productId: productId)
            await MainActor.run { completion(code) }
        }
    }

    @objc public static func isOwned(productId: String, completion: @escaping (Int) -> Void) {
        Task {
            let code = await ownedCode(productId: productId)
            await MainActor.run { completion(code) }
        }
    }

    /// 購入の復元(App Store と同期してから所有を確かめる。サインインを求められることがある)。
    @objc public static func restore(productId: String, completion: @escaping (Int) -> Void) {
        Task {
            do {
                try await AppStore.sync()
            } catch {
                await MainActor.run { completion(-1) }
                return
            }
            let code = await ownedCode(productId: productId)
            await MainActor.run { completion(code) }
        }
    }

    private static func purchaseCode(productId: String) async -> Int {
        guard let product = try? await Product.products(for: [productId]).first else {
            return 4
        }
        do {
            switch try await product.purchase() {
            case .success(let verification):
                guard case .verified(let transaction) = verification else {
                    return 3
                }
                await transaction.finish()
                return 0
            case .userCancelled:
                return 1
            case .pending:
                return 2
            @unknown default:
                return 3
            }
        } catch {
            return 3
        }
    }

    private static func ownedCode(productId: String) async -> Int {
        // 返金・取り消しされた購入は currentEntitlements に含まれない
        for await result in Transaction.currentEntitlements {
            if case .verified(let transaction) = result, transaction.productID == productId, transaction.revocationDate == nil {
                return 1
            }
        }
        return 0
    }
}
