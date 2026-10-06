import Foundation
import Observation
import UsageCore

@MainActor @Observable final class MonitorModel {
    var accounts: [AccountSummary] = []
    var refreshing = false
    var issue: MonitorError?
    var storageAvailable = false
    var authenticating = false
    private let repository = AccountRepository.shared

    func load() async {
        do {
            accounts = try await repository.summaries()
            storageAvailable = true
        } catch {
            storageAvailable = false
            issue = .storage
        }
    }

    func refresh() async {
        guard !refreshing, !authenticating else { return }
        refreshing = true
        defer { refreshing = false }
        do {
            accounts = try await repository.refreshAll()
            storageAvailable = true
        } catch is CancellationError { }
        catch MonitorError.busy { }
        catch { issue = MonitorError.from(error) }
    }

    func remove(_ account: AccountSummary) async {
        do {
            try await repository.remove(account.id)
            await load()
        } catch {
            issue = MonitorError.from(error)
            // Reload even when publishing the widget snapshot failed after a successful vault write.
            await load()
        }
    }
}
