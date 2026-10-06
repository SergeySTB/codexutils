import Foundation
import UsageCore
import WidgetKit

actor AccountRepository {
    static let shared = AccountRepository()
    private let vault = KeychainVault()
    private let client = ProviderClient(transport: SecureTransport())
    private var refreshing = false

    func summaries() throws -> [AccountSummary] { try vault.load().map(\.summary) }

    func saveCodex(_ signedIn: SignedInAccount, replacing id: UUID?) throws {
        guard !refreshing else { throw MonitorError.busy }
        var accounts = try vault.load()
        let index: Int?
        if let id {
            guard let found = accounts.firstIndex(where: { $0.summary.id == id }),
                  accounts[found].summary.provider == .codex,
                  accounts[found].credentials.accountID == signedIn.identity.accountID else {
                throw MonitorError.accountMismatch
            }
            index = found
        } else {
            index = accounts.firstIndex { $0.summary.provider == .codex && $0.credentials.accountID == signedIn.identity.accountID }
        }
        var summary = index.map { accounts[$0].summary } ?? AccountSummary(provider: .codex,
            name: signedIn.identity.email.isEmpty ? "Codex" : signedIn.identity.email)
        summary.plan = signedIn.identity.plan
        summary.issue = nil
        summary.retryAt = nil
        let account = StoredAccount(summary: summary, credentials: signedIn.credentials)
        if let index { accounts[index] = account } else { accounts.append(account) }
        try persist(accounts)
    }

    func saveClaude(name: String, token: String, replacing id: UUID?) throws {
        guard !refreshing else { throw MonitorError.busy }
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let token = token.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= 60, TokenIdentity.validClaudeToken(token) else { throw MonitorError.invalidToken }
        var accounts = try vault.load()
        var summary = AccountSummary(provider: .claude, name: name)
        if let id {
            guard let index = accounts.firstIndex(where: { $0.summary.id == id && $0.summary.provider == .claude }) else {
                throw MonitorError.accountMismatch
            }
            summary = accounts[index].summary
            summary.name = name
            summary.issue = nil
            summary.retryAt = nil
            accounts[index] = StoredAccount(summary: summary, credentials: Credentials(accessToken: token))
        } else {
            accounts.append(StoredAccount(summary: summary, credentials: Credentials(accessToken: token)))
        }
        try persist(accounts)
    }

    func remove(_ id: UUID) throws {
        guard !refreshing else { throw MonitorError.busy }
        var accounts = try vault.load()
        accounts.removeAll { $0.summary.id == id }
        try persist(accounts)
    }

    func refreshAll() async throws -> [AccountSummary] {
        guard !refreshing else { throw MonitorError.busy }
        refreshing = true
        defer { refreshing = false }
        var accounts = try vault.load()
        if accounts.isEmpty { return [] }
        for index in accounts.indices {
            try Task.checkCancellation()
            if let retry = accounts[index].summary.retryAt, retry > Date() { continue }
            do {
                if accounts[index].summary.provider == .codex,
                   accounts[index].credentials.expiresAt <= Date().addingTimeInterval(300) {
                    let updated = try await client.refresh(accounts[index].credentials)
                    accounts[index].credentials = updated.credentials
                    accounts[index].summary.plan = updated.identity.plan
                    // Save a rotated refresh token before attempting any further network request.
                    try vault.save(accounts)
                }
                let usage: Usage
                do {
                    usage = try await client.usage(provider: accounts[index].summary.provider, credentials: accounts[index].credentials)
                } catch MonitorError.signInRequired where accounts[index].summary.provider == .codex {
                    let updated = try await client.refresh(accounts[index].credentials)
                    accounts[index].credentials = updated.credentials
                    accounts[index].summary.plan = updated.identity.plan
                    try vault.save(accounts)
                    usage = try await client.usage(provider: .codex, credentials: updated.credentials)
                }
                accounts[index].summary.usage = usage
                accounts[index].summary.updatedAt = Date()
                accounts[index].summary.issue = nil
            } catch is CancellationError {
                throw CancellationError()
            } catch MonitorError.storage {
                throw MonitorError.storage
            } catch {
                if Task.isCancelled { throw CancellationError() }
                // Keep the last successful reading; never present a failed request as zero.
                accounts[index].summary.issue = MonitorError.from(error)
            }
            accounts[index].summary.retryAt = RefreshPolicy.deadline(provider: accounts[index].summary.provider,
                issue: accounts[index].summary.issue, now: Date())
            try persist(accounts)
        }
        try publish(accounts)
        return accounts.map(\.summary)
    }

    private func persist(_ accounts: [StoredAccount]) throws {
        try vault.save(accounts)
        try publish(accounts)
    }

    private func publish(_ accounts: [StoredAccount]) throws {
        do { try SnapshotStore.write(accounts.map(\.summary)) }
        catch { throw MonitorError.storage }
        WidgetCenter.shared.reloadAllTimelines()
    }
}
