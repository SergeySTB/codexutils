import Foundation
import Security
import UsageCore

struct StoredAccount: Codable, Sendable {
    var summary: AccountSummary
    var credentials: Credentials
}

struct KeychainVault {
    private var query: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: Bundle.main.bundleIdentifier ?? "AIUsageMonitor",
         kSecAttrAccount as String: "accounts-v1"]
    }

    func load() throws -> [StoredAccount] {
        var request = query
        request[kSecReturnData as String] = true
        request[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(request as CFDictionary, &result)
        if status == errSecItemNotFound { return [] }
        guard status == errSecSuccess, let data = result as? Data else { throw MonitorError.storage }
        do { return try JSONDecoder().decode([StoredAccount].self, from: data) }
        catch { throw MonitorError.storage }
    }

    func save(_ accounts: [StoredAccount]) throws {
        let data = try JSONEncoder().encode(accounts)
        let attributes: [String: Any] = [kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        let status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if status == errSecItemNotFound {
            guard SecItemAdd(query.merging(attributes) { _, new in new } as CFDictionary, nil) == errSecSuccess else {
                throw MonitorError.storage
            }
        } else if status != errSecSuccess { throw MonitorError.storage }
    }
}
