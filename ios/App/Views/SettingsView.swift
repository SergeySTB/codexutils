import SwiftUI
import UsageCore
import WidgetKit

struct SettingsView: View {
    @Binding var language: String
    @State private var minutes = SharedSettings.refreshMinutes
    @State private var scheduleFailed = false

    var body: some View {
        Form {
            Section(L10n.text("language")) {
                Picker(L10n.text("language"), selection: $language) {
                    Text(L10n.text("system_language")).tag("system")
                    Text("English").tag("en")
                    Text("Русский").tag("ru")
                }
                .onChange(of: language) { _, value in
                    SharedSettings.language = value
                    WidgetCenter.shared.reloadAllTimelines()
                }
            }
            Section {
                Picker(L10n.text("refresh_interval"), selection: $minutes) {
                    ForEach(RefreshPolicy.intervals, id: \.self) { value in
                        Text(Duration.seconds(value * 60).formatted(.units(allowed: [.hours, .minutes], width: .wide))).tag(value)
                    }
                }.onChange(of: minutes) { _, value in
                    SharedSettings.refreshMinutes = value
                    scheduleFailed = !BackgroundRefresh.schedule()
                    WidgetCenter.shared.reloadAllTimelines()
                }
                if scheduleFailed { Text(L10n.text("schedule_failed")).foregroundStyle(.orange) }
            } header: { Text(L10n.text("background_refresh")) } footer: {
                Text(L10n.text("background_hint"))
            }
            Section(L10n.text("privacy")) {
                Label(L10n.text("keychain_hint"), systemImage: "lock.shield")
                Text(L10n.text("widget_privacy_hint")).font(.footnote).foregroundStyle(.secondary)
            }
            Section {
                LabeledContent(L10n.text("version"), value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "—")
                Text(L10n.text("unofficial_hint")).font(.footnote).foregroundStyle(.secondary)
            }
        }.navigationTitle(L10n.text("settings"))
    }
}
