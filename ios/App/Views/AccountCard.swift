import SwiftUI
import UsageCore

struct AccountCard: View {
    let account: AccountSummary
    let busy: Bool
    let reconnect: () -> Void
    let remove: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 16) { UsageRings(account: account); heading; Spacer(minLength: 0) }
                VStack(alignment: .leading, spacing: 12) { UsageRings(account: account); heading }
            }
            if let window = account.usage?.fiveHour {
                LimitRow(title: L10n.text("five_hours"), window: window, color: MonitorPalette.session)
            }
            if let window = account.usage?.weekly {
                LimitRow(title: L10n.text("week"), window: window, color: MonitorPalette.weekly)
            }
            if account.usage?.fiveHour == nil && account.usage?.weekly == nil {
                Label(L10n.text("no_limits"), systemImage: "minus.circle").font(.subheadline).foregroundStyle(.secondary)
            }
            if let issue = account.issue {
                Label(L10n.text("error_" + issue.rawValue), systemImage: "exclamationmark.triangle")
                    .font(.callout).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                if issue == .signInRequired || issue == .invalidToken {
                    Button(L10n.text("renew"), systemImage: "person.crop.circle.badge.checkmark", action: reconnect)
                        .buttonStyle(.bordered).disabled(busy)
                }
            }
            ViewThatFits(in: .horizontal) {
                HStack { FreshnessLabel(account: account); Spacer(); actions }
                VStack(alignment: .leading) { FreshnessLabel(account: account); actions }
            }
        }
        .padding(20)
        .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 24))
        .accessibilityElement(children: .contain)
    }

    private var heading: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(account.provider.title.uppercased()).font(.caption.weight(.bold)).tracking(1.5).foregroundStyle(.secondary)
            Text(account.name).font(.title3.weight(.semibold)).textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
            if !account.plan.isEmpty { Text(account.plan.capitalized).font(.subheadline).foregroundStyle(.secondary) }
        }
    }

    private var actions: some View {
        Menu {
            Button(L10n.text("renew"), systemImage: "person.crop.circle.badge.checkmark", action: reconnect)
            Button(L10n.text("remove"), systemImage: "trash", role: .destructive, action: remove)
        } label: {
            Image(systemName: "ellipsis").frame(minWidth: 44, minHeight: 44).contentShape(Rectangle())
        }.disabled(busy).accessibilityLabel(L10n.text("account_actions") + ": " + account.name)
    }
}
