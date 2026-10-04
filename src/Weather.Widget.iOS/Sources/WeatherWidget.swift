import SwiftUI
import WidgetKit

/// アプリ(MAUI)が App Group に書く widget.json(Weather.Presentation.Background.WidgetSnapshot)。
/// 文字列は書式済みで、この拡張では取得も換算もしない。出典(credit)は常に表示する。
struct WidgetSnapshot: Codable {
    let state: String
    let placeName: String
    let temperature: String
    let icon: String
    let conditionText: String
    let highLow: String?
    let precipitation: String?
    let alertText: String?
    let attribution: String
    let credit: String
    let updatedText: String
    let isStale: Bool
    let backgroundRefresh: Bool
    let note: String?
}

enum SnapshotStore {
    /// アプリ側の IosBackgroundPlatform.AppGroupId と一致させる。
    static let appGroup = "group.com.weatherapp.soramoyou"

    static func load() -> WidgetSnapshot? {
        guard let url = FileManager.default
            .containerURL(forSecurityApplicationGroupIdentifier: appGroup)?
            .appendingPathComponent("widget.json"),
            let data = try? Data(contentsOf: url)
        else {
            return nil
        }
        return try? JSONDecoder().decode(WidgetSnapshot.self, from: data)
    }
}

struct WeatherEntry: TimelineEntry {
    let date: Date
    let snapshot: WidgetSnapshot?
}

struct WeatherProvider: TimelineProvider {
    func placeholder(in context: Context) -> WeatherEntry {
        WeatherEntry(date: Date(), snapshot: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (WeatherEntry) -> Void) {
        completion(WeatherEntry(date: Date(), snapshot: SnapshotStore.load()))
    }

    /// アプリから再読み込みを指示する WidgetCenter は Swift の API のため .NET からは呼べない。30 分ごとにファイルを読み直す。
    func getTimeline(in context: Context, completion: @escaping (Timeline<WeatherEntry>) -> Void) {
        let entry = WeatherEntry(date: Date(), snapshot: SnapshotStore.load())
        completion(Timeline(entries: [entry], policy: .after(Date().addingTimeInterval(30 * 60))))
    }
}

struct WeatherWidgetView: View {
    let entry: WeatherEntry

    private var isJapanese: Bool {
        Locale.preferredLanguages.first?.hasPrefix("ja") ?? true
    }

    var body: some View {
        if let s = entry.snapshot {
            VStack(alignment: .leading, spacing: 2) {
                Text(s.placeName).font(.caption).bold().lineLimit(1)
                if s.state == "Ready" {
                    HStack(spacing: 4) {
                        Text(s.icon).font(.title2)
                        Text(s.temperature).font(.title).bold()
                    }
                }
                Text(s.conditionText).font(.caption2).lineLimit(2)
                if let highLow = s.highLow {
                    Text(highLow).font(.caption2).lineLimit(1)
                }
                if let alert = s.alertText {
                    Text(alert).font(.caption2).bold().foregroundStyle(.yellow).lineLimit(1)
                }
                Spacer(minLength: 0)
                Text(s.credit).font(.system(size: 9)).opacity(0.85).lineLimit(1)
                Text([s.attribution, s.updatedText].filter { !$0.isEmpty }.joined(separator: " · "))
                    .font(.system(size: 9)).opacity(0.85).lineLimit(1)
                if let note = s.note {
                    Text(note).font(.system(size: 9)).opacity(0.85).lineLimit(2)
                }
            }
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        } else {
            VStack(alignment: .leading, spacing: 4) {
                Text(isJapanese ? "空模様" : "Soramoyou").font(.caption).bold()
                Text(isJapanese ? "アプリを開くと表示されます" : "Open the app to show the weather").font(.caption2)
            }
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }
}

@main
struct WeatherWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "WeatherWidget", provider: WeatherProvider()) { entry in
            WeatherWidgetView(entry: entry)
                .containerBackground(for: .widget) {
                    LinearGradient(
                        colors: [Color(red: 0.11, green: 0.17, blue: 0.35), Color(red: 0.18, green: 0.42, blue: 0.80)],
                        startPoint: .top,
                        endPoint: .bottom)
                }
        }
        .configurationDisplayName("空模様")
        .description("お気に入りの先頭の地点の天気と出典を表示します")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}
