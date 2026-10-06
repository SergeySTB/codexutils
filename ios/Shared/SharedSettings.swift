import Foundation
import UsageCore

enum SharedSettings {
    static var groupID: String { Bundle.main.object(forInfoDictionaryKey: "SharedAppGroup") as? String ?? "" }
    static var defaults: UserDefaults { UserDefaults(suiteName: groupID) ?? .standard }
    static var refreshMinutes: Int {
        get { RefreshPolicy.normalized(defaults.integer(forKey: "refreshMinutes")) }
        set { defaults.set(RefreshPolicy.normalized(newValue), forKey: "refreshMinutes") }
    }
    static var language: String {
        get { defaults.string(forKey: "language") ?? "system" }
        set { defaults.set(["system", "en", "ru"].contains(newValue) ? newValue : "system", forKey: "language") }
    }
    static var languageCode: String {
        language == "system" ? (Locale.preferredLanguages.first?.hasPrefix("ru") == true ? "ru" : "en") : language
    }
    static var locale: Locale { Locale(identifier: languageCode) }
}

enum L10n {
    static func text(_ key: String) -> String {
        guard let path = Bundle.main.path(forResource: SharedSettings.languageCode, ofType: "lproj"),
              let bundle = Bundle(path: path) else { return key }
        return bundle.localizedString(forKey: key, value: key, table: nil)
    }
}

enum SnapshotStore {
    private static func url() throws -> URL {
        guard let root = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: SharedSettings.groupID) else {
            throw MonitorError.storage
        }
        return root.appendingPathComponent("usage-snapshot-v1.json")
    }
    static func read() throws -> [AccountSummary] {
        let file = try url()
        if !FileManager.default.fileExists(atPath: file.path) { return [] }
        return try JSONDecoder().decode([AccountSummary].self, from: Data(contentsOf: file))
    }
    static func write(_ accounts: [AccountSummary]) throws {
        let file = try url()
        try JSONEncoder().encode(accounts).write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        var mutable = file
        try mutable.setResourceValues(values)
    }
}
