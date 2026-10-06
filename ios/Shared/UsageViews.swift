import SwiftUI
import UsageCore

enum MonitorPalette {
    static let session = Color.teal
    static let weekly = Color.purple
}

struct UsageRings: View {
    let account: AccountSummary
    var diameter: CGFloat = 76

    var body: some View {
        ZStack {
            ring(account.usage?.fiveHour, color: MonitorPalette.session, inset: 0)
            ring(account.usage?.weekly, color: MonitorPalette.weekly, inset: diameter * 0.12)
            VStack(spacing: 0) {
                Image(account.provider.rawValue)
                    .resizable().scaledToFit().frame(width: diameter * 0.32, height: diameter * 0.32)
                    .accessibilityHidden(true)
                Text(String(account.name.prefix(1)).uppercased())
                    .font(.system(size: diameter * 0.16, weight: .bold, design: .rounded))
            }
        }
        .frame(width: diameter, height: diameter)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(account.provider.title + ", " + account.name)
        .accessibilityValue(L10n.text("five_hours") + ": " + value(account.usage?.fiveHour) + ", " + L10n.text("week") + ": " + value(account.usage?.weekly))
    }

    private func value(_ window: UsageWindow?) -> String {
        window.map { "\($0.remaining)% " + L10n.text("remaining") } ?? L10n.text("no_data")
    }

    private func ring(_ window: UsageWindow?, color: Color, inset: CGFloat) -> some View {
        ZStack {
            Circle().stroke(color.opacity(0.12), lineWidth: diameter * 0.065)
            if let window {
                Circle().trim(from: 0, to: Double(window.remaining) / 100)
                    .stroke(color, style: StrokeStyle(lineWidth: diameter * 0.065, lineCap: .round))
                    .rotationEffect(.degrees(-90))
            }
        }.padding(inset + diameter * 0.04)
    }
}

struct LimitRow: View {
    let title: String
    let window: UsageWindow
    let color: Color
    var compact = false

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack(alignment: .firstTextBaseline) {
                Text(title).foregroundStyle(.secondary)
                Spacer(minLength: 4)
                Text("\(window.remaining)%").fontWeight(.semibold).monospacedDigit()
            }.font(compact ? .caption : .subheadline)
            ProgressView(value: Double(window.remaining), total: 100).tint(color)
                .accessibilityLabel(title + ", " + L10n.text("remaining"))
            if let reset = window.resetsAt, !compact {
                HStack(spacing: 4) {
                    Text(L10n.text("resets"))
                    Text(reset, format: .dateTime.month(.abbreviated).day().hour().minute())
                }.font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}

struct FreshnessLabel: View {
    let account: AccountSummary
    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: account.isStale(interval: TimeInterval(SharedSettings.refreshMinutes * 60)) ? "clock.badge.exclamationmark" : "clock")
            if let updated = account.updatedAt {
                Text(L10n.text(account.isStale(interval: TimeInterval(SharedSettings.refreshMinutes * 60)) ? "outdated" : "updated"))
                Text(updated, style: .relative)
            } else { Text(L10n.text("no_data")) }
        }.font(.caption2).foregroundStyle(.secondary)
    }
}
