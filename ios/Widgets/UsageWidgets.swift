import SwiftUI
import WidgetKit
import UsageCore

struct UsageEntry: TimelineEntry {
    let date: Date
    let accounts: [AccountSummary]
    var unavailable = false
}

struct UsageTimeline: TimelineProvider {
    func placeholder(in context: Context) -> UsageEntry { preview }
    func getSnapshot(in context: Context, completion: @escaping (UsageEntry) -> Void) {
        completion(context.isPreview ? preview : current())
    }
    func getTimeline(in context: Context, completion: @escaping (Timeline<UsageEntry>) -> Void) {
        let entry = current()
        // The app owns network access and token rotation. Background app refresh publishes
        // fresh snapshots and reloads timelines; this tick updates age/staleness offline.
        let next = Date().addingTimeInterval(TimeInterval(SharedSettings.refreshMinutes * 60))
        completion(Timeline(entries: [entry], policy: .after(next)))
    }
    private func current() -> UsageEntry {
        do { return UsageEntry(date: Date(), accounts: try SnapshotStore.read()) }
        catch { return UsageEntry(date: Date(), accounts: [], unavailable: true) }
    }
    private var preview: UsageEntry {
        var sample = AccountSummary(provider: .codex, name: L10n.text("sample_account"), plan: "Plus")
        sample.usage = Usage(fiveHour: UsageWindow(remaining: 74), weekly: UsageWindow(remaining: 48))
        sample.updatedAt = Date()
        return UsageEntry(date: Date(), accounts: [sample])
    }
}

struct IconsWidgetView: View {
    let entry: UsageEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        Group {
            if entry.accounts.isEmpty {
                WidgetEmptyView(unavailable: entry.unavailable)
            } else if family == .accessoryCircular {
                if let account = entry.accounts.first {
                    if let window = account.usage?.fiveHour {
                        Gauge(value: Double(window.remaining), in: 0...100) {
                            Text(String(account.name.prefix(1)))
                        } currentValueLabel: { Text("\(window.remaining)") }
                            .gaugeStyle(.accessoryCircular)
                            .accessibilityLabel(account.name + ", " + L10n.text("five_hours"))
                    } else {
                        Text("—").accessibilityLabel(account.name + ", " + L10n.text("no_data"))
                    }
                }
            } else {
                VStack(alignment: .leading, spacing: 10) {
                    Text(L10n.text("remaining_limits")).font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                    HStack(spacing: 12) {
                        ForEach(Array(entry.accounts.prefix(capacity))) { account in
                            VStack(spacing: 5) {
                                UsageRings(account: account, diameter: family == .systemSmall ? 70 : 64)
                                Text(account.name).font(.caption2).lineLimit(1)
                                if account.issue != nil { Image(systemName: "exclamationmark.triangle").font(.caption2).foregroundStyle(.orange) }
                            }.frame(maxWidth: .infinity)
                        }
                        if entry.accounts.count > capacity {
                            Text("+\(entry.accounts.count - capacity)").font(.caption.bold())
                                .accessibilityLabel(L10n.text("more_accounts") + ": \(entry.accounts.count - capacity)")
                        }
                    }
                    if let account = entry.accounts.first { FreshnessLabel(account: account).lineLimit(1) }
                }
            }
        }
        .environment(\.locale, SharedSettings.locale)
        .privacySensitive()
        .widgetURL(URL(string: "aiusagemonitor://overview"))
        .containerBackground(.background, for: .widget)
    }
    private var capacity: Int { family == .systemSmall ? 1 : 3 }
}

struct CardsWidgetView: View {
    let entry: UsageEntry
    @Environment(\.widgetFamily) private var family
    private var capacity: Int { family == .systemLarge ? 3 : 1 }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if entry.accounts.isEmpty { WidgetEmptyView(unavailable: entry.unavailable) }
            ForEach(Array(entry.accounts.prefix(capacity))) { account in
                VStack(alignment: .leading, spacing: 7) {
                    HStack {
                        Text(account.provider.title + " · " + account.name).font(.caption.weight(.semibold)).lineLimit(1)
                        Spacer(minLength: 0)
                        if account.issue != nil { Image(systemName: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange) }
                    }
                    if let window = account.usage?.fiveHour {
                        LimitRow(title: L10n.text("five_hours"), window: window, color: .teal, compact: true)
                    }
                    if let window = account.usage?.weekly {
                        LimitRow(title: L10n.text("week"), window: window, color: .purple, compact: true)
                    }
                    FreshnessLabel(account: account).lineLimit(1)
                }
            }
            if entry.accounts.count > capacity {
                Text("+\(entry.accounts.count - capacity) " + L10n.text("more_accounts")).font(.caption2).foregroundStyle(.secondary)
            }
        }
        .environment(\.locale, SharedSettings.locale)
        .privacySensitive()
        .widgetURL(URL(string: "aiusagemonitor://overview"))
        .containerBackground(.background, for: .widget)
    }
}

struct WidgetEmptyView: View {
    let unavailable: Bool
    var body: some View {
        VStack(spacing: 8) {
            Image(systemName: unavailable ? "lock" : "plus.circle").font(.title2)
            Text(L10n.text(unavailable ? "widget_unavailable" : "widget_empty")).font(.caption).multilineTextAlignment(.center)
        }.frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

struct IconsWidget: Widget {
    let kind = "AIUsageMonitor.Icons"
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: UsageTimeline()) { IconsWidgetView(entry: $0) }
            .configurationDisplayName(L10n.text("icons_widget"))
            .description(L10n.text("icons_description"))
            .supportedFamilies([.systemSmall, .systemMedium, .accessoryCircular])
    }
}

struct CardsWidget: Widget {
    let kind = "AIUsageMonitor.Cards"
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: UsageTimeline()) { CardsWidgetView(entry: $0) }
            .configurationDisplayName(L10n.text("cards_widget"))
            .description(L10n.text("cards_description"))
            .supportedFamilies([.systemSmall, .systemMedium, .systemLarge])
    }
}

@main struct UsageWidgets: WidgetBundle {
    var body: some Widget { IconsWidget(); CardsWidget() }
}
