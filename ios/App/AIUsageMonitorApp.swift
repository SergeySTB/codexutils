import SwiftUI

@main @MainActor struct AIUsageMonitorApp: App {
    @Environment(\.scenePhase) private var phase
    @State private var model = MonitorModel()

    init() { BackgroundRefresh.register() }

    var body: some Scene {
        WindowGroup {
            DashboardView(model: model)
                .task(id: phase) {
                    guard phase == .active else { return }
                    await model.load()
                    while !Task.isCancelled {
                        await model.refresh()
                        do { try await Task.sleep(for: .seconds(60)) }
                        catch { return }
                    }
                }
                .onChange(of: phase) { _, value in
                    if value == .background { BackgroundRefresh.schedule() }
                }
        }
    }
}
