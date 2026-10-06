import Foundation

public enum Provider: String, Codable, CaseIterable, Sendable {
    case codex, claude
    public var title: String { self == .codex ? "Codex" : "Claude" }
    public var minimumInterval: TimeInterval { self == .claude ? 300 : 60 }
}

public struct UsageWindow: Codable, Equatable, Sendable {
    public let remaining: Int
    public let resetsAt: Date?
    public init(remaining: Int, resetsAt: Date? = nil) {
        self.remaining = max(0, min(100, remaining))
        self.resetsAt = resetsAt
    }
}

public struct Usage: Codable, Equatable, Sendable {
    public var fiveHour: UsageWindow?
    public var weekly: UsageWindow?
    public init(fiveHour: UsageWindow? = nil, weekly: UsageWindow? = nil) {
        self.fiveHour = fiveHour
        self.weekly = weekly
    }
}

// This is the only account type exported to the widget's App Group container.
public struct AccountSummary: Codable, Identifiable, Equatable, Sendable {
    public let id: UUID
    public let provider: Provider
    public var name: String
    public var plan: String
    public var usage: Usage?
    public var updatedAt: Date?
    public var retryAt: Date?
    public var issue: MonitorError?

    public init(id: UUID = UUID(), provider: Provider, name: String, plan: String = "") {
        self.id = id
        self.provider = provider
        self.name = name
        self.plan = plan
    }

    public func isStale(at now: Date = Date(), interval: TimeInterval = 1800) -> Bool {
        issue != nil || updatedAt.map { now.timeIntervalSince($0) > max(300, interval * 2) } ?? true
    }
}

public struct Credentials: Codable, Equatable, Sendable {
    public var accessToken: String
    public var refreshToken: String
    public var idToken: String
    public var accountID: String
    public var expiresAt: Date

    public init(accessToken: String, refreshToken: String = "", idToken: String = "",
                accountID: String = "", expiresAt: Date = .distantFuture) {
        self.accessToken = accessToken
        self.refreshToken = refreshToken
        self.idToken = idToken
        self.accountID = accountID
        self.expiresAt = expiresAt
    }
}

public enum MonitorError: String, Error, Codable, Sendable {
    case invalidResponse, invalidToken, accountMismatch, signInRequired, forbidden
    case rateLimited, network, storage, timedOut, busy

    public static func from(_ error: Error) -> MonitorError {
        if let value = error as? MonitorError { return value }
        return .network
    }
}

public enum RefreshPolicy {
    public static let intervals = [15, 30, 60, 120, 360, 720, 1440]
    public static func normalized(_ value: Int) -> Int { intervals.contains(value) ? value : 30 }
    public static func deadline(provider: Provider, issue: MonitorError?, now: Date) -> Date {
        now.addingTimeInterval(issue == .rateLimited ? 900 : provider.minimumInterval)
    }
}
