import Foundation

public struct TokenIdentity: Sendable {
    public let accountID: String
    public let email: String
    public let plan: String
    public let expiresAt: Date

    // Claims are display/routing metadata, not local proof of authentication.
    // The provider validates the bearer token on each usage request.
    public static func read(idToken: String, accessToken: String, now: Date = Date()) throws -> TokenIdentity {
        let id = try payload(idToken)
        let access = (try? payload(accessToken)) ?? [:]
        let idAuth = id["https://api.openai.com/auth"] as? [String: Any] ?? [:]
        let accessAuth = access["https://api.openai.com/auth"] as? [String: Any] ?? [:]
        let first = idAuth["chatgpt_account_id"] as? String ?? ""
        let second = accessAuth["chatgpt_account_id"] as? String ?? ""
        guard first.isEmpty || second.isEmpty || first == second else { throw MonitorError.accountMismatch }
        let accountID = first.isEmpty ? second : first
        guard !accountID.isEmpty, accountID.count <= 256,
              !accountID.contains(where: { $0.isNewline || $0.asciiValue.map { $0 < 32 } == true }) else {
            throw MonitorError.invalidToken
        }
        let profile = id["https://api.openai.com/profile"] as? [String: Any] ?? [:]
        return TokenIdentity(accountID: accountID,
                             email: id["email"] as? String ?? profile["email"] as? String ?? "",
                             plan: idAuth["chatgpt_plan_type"] as? String ?? accessAuth["chatgpt_plan_type"] as? String ?? "",
                             expiresAt: UsageParser.timestamp(UsageParser.number(access["exp"])) ?? now.addingTimeInterval(3600))
    }

    public static func validClaudeToken(_ token: String) -> Bool {
        token.count <= 4096 && token.range(of: "^sk-ant-oat[0-9A-Za-z_-]+$", options: .regularExpression) != nil
    }

    private static func payload(_ token: String) throws -> [String: Any] {
        let parts = token.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3, !parts[1].isEmpty, token.count <= 32768 else { throw MonitorError.invalidToken }
        var encoded = String(parts[1]).replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        encoded += String(repeating: "=", count: (4 - encoded.count % 4) % 4)
        guard let data = Data(base64Encoded: encoded),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw MonitorError.invalidToken }
        return object
    }
}
