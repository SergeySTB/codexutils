import SwiftUI
import UsageCore

struct ConnectionRoute: Identifiable {
    let id = UUID()
    let provider: Provider
    var account: AccountSummary?
}

struct DashboardView: View {
    @Bindable var model: MonitorModel
    @State private var route: ConnectionRoute?
    @State private var removal: AccountSummary?
    @State private var language = SharedSettings.language
    @Environment(\.scenePhase) private var phase

    var body: some View {
        TabView {
            NavigationStack {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 18) {
                        Text(L10n.text("dashboard_subtitle"))
                            .font(.subheadline).foregroundStyle(.secondary)
                        if !model.storageAvailable {
                            ContentUnavailableView(L10n.text("storage_title"), systemImage: "lock.trianglebadge.exclamationmark",
                                description: Text(L10n.text("error_storage")))
                        } else if model.accounts.isEmpty {
                            ContentUnavailableView {
                                Label(L10n.text("empty_title"), systemImage: "gauge.with.dots.needle.33percent")
                            } description: {
                                Text(L10n.text("empty_body"))
                            } actions: { addMenu }
                        } else {
                            ForEach(model.accounts) { account in
                                AccountCard(account: account, busy: model.refreshing,
                                    reconnect: { route = ConnectionRoute(provider: account.provider, account: account) },
                                    remove: { removal = account })
                            }
                        }
                    }.padding(20).frame(maxWidth: 720).frame(maxWidth: .infinity)
                }
                .background(Color(.systemGroupedBackground))
                .navigationTitle(L10n.text("overview"))
                .refreshable { await model.refresh() }
                .toolbar {
                    ToolbarItem(placement: .topBarLeading) {
                        Button { Task { await model.refresh() } } label: {
                            if model.refreshing { ProgressView() } else { Image(systemName: "arrow.clockwise") }
                        }.disabled(model.refreshing).accessibilityLabel(L10n.text("refresh"))
                    }
                    ToolbarItem(placement: .topBarTrailing) { addMenu }
                }
            }.tabItem { Label(L10n.text("overview"), systemImage: "chart.donut") }

            NavigationStack { WidgetGallery(accounts: model.accounts) }
                .tabItem { Label(L10n.text("widgets"), systemImage: "square.grid.2x2") }

            NavigationStack { SettingsView(language: $language) }
                .tabItem { Label(L10n.text("settings"), systemImage: "slider.horizontal.3") }
        }
        .tint(.teal)
        .environment(\.locale, SharedSettings.locale)
        .sheet(item: $route, onDismiss: {
            model.authenticating = false
            Task { await model.load(); await model.refresh() }
        }) { route in
            ConnectionView(route: route).onAppear { model.authenticating = true }
        }
        .confirmationDialog(L10n.text("remove_title"), isPresented: Binding(
            get: { removal != nil }, set: { if !$0 { removal = nil } }
        ), titleVisibility: .visible, presenting: removal) { account in
            Button(L10n.text("remove"), role: .destructive) { Task { await model.remove(account) } }
        } message: { account in Text(account.name + "\n" + L10n.text("remove_body")) }
        .alert(L10n.text("attention"), isPresented: Binding(
            get: { model.issue != nil }, set: { if !$0 { model.issue = nil } }
        )) { Button(L10n.text("ok"), role: .cancel) { model.issue = nil } } message: {
            Text(L10n.text("error_" + (model.issue?.rawValue ?? "network")))
        }
        // Hide account information from the app switcher's snapshot.
        .privacySensitive()
        .overlay {
            if phase == .background { Color(.systemBackground).ignoresSafeArea().overlay(Image(systemName: "lock.fill").font(.largeTitle)) }
        }
    }

    private var addMenu: some View {
        Menu {
            Button(L10n.text("add_codex")) { route = ConnectionRoute(provider: .codex) }
            Button(L10n.text("add_claude")) { route = ConnectionRoute(provider: .claude) }
        } label: { Label(L10n.text("add_account"), systemImage: "plus") }
            .disabled(!model.storageAvailable || model.refreshing)
    }
}
