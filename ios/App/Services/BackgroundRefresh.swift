import BackgroundTasks
import Foundation
import UsageCore

enum BackgroundRefresh {
    static var identifier: String { (Bundle.main.bundleIdentifier ?? "") + ".refresh" }

    static func register() {
        BGTaskScheduler.shared.register(forTaskWithIdentifier: identifier, using: nil) { task in
            schedule()
            let work = Task {
                do {
                    _ = try await AccountRepository.shared.refreshAll()
                    task.setTaskCompleted(success: true)
                } catch { task.setTaskCompleted(success: false) }
            }
            task.expirationHandler = { work.cancel() }
        }
    }

    @discardableResult static func schedule() -> Bool {
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: identifier)
        let request = BGAppRefreshTaskRequest(identifier: identifier)
        request.earliestBeginDate = Date().addingTimeInterval(TimeInterval(SharedSettings.refreshMinutes * 60))
        do { try BGTaskScheduler.shared.submit(request); return true }
        catch { return false }
    }
}
