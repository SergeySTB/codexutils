import SwiftUI
import UsageCore

struct WidgetGallery: View {
    let accounts: [AccountSummary]
    private var samples: [AccountSummary] {
        if !accounts.isEmpty { return accounts }
        var sample = AccountSummary(provider: .codex, name: L10n.text("sample_account"), plan: "Plus")
        sample.usage = Usage(fiveHour: UsageWindow(remaining: 74), weekly: UsageWindow(remaining: 48))
        sample.updatedAt = Date()
        return [sample]
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                Text(L10n.text("widget_setup")).foregroundStyle(.secondary)
                if accounts.isEmpty { Label(L10n.text("sample_data"), systemImage: "sparkles").font(.caption) }
                Text(L10n.text("icons")).font(.title2.bold())
                HStack(spacing: 12) {
                    ForEach(Array(samples.prefix(3))) { account in UsageRings(account: account, diameter: 60) }
                }.padding(20).frame(maxWidth: .infinity)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 24))
                Text(L10n.text("cards")).font(.title2.bold())
                if let account = samples.first {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(account.provider.title + " · " + account.name).font(.headline)
                        if let window = account.usage?.fiveHour { LimitRow(title: L10n.text("five_hours"), window: window, color: .teal, compact: true) }
                        if let window = account.usage?.weekly { LimitRow(title: L10n.text("week"), window: window, color: .purple, compact: true) }
                        FreshnessLabel(account: account)
                    }.padding(20).background(.regularMaterial, in: RoundedRectangle(cornerRadius: 24))
                }
                Text(L10n.text("background_hint")).font(.footnote).foregroundStyle(.secondary)
            }.padding(20).frame(maxWidth: 720).frame(maxWidth: .infinity)
        }.background(Color(.systemGroupedBackground)).navigationTitle(L10n.text("widgets"))
    }
}
