import Foundation
import CoreFoundation

public enum UsageParser {
    public static func parse(_ data: Data, provider: Provider) throws -> Usage {
        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw MonitorError.invalidResponse
        }
        if provider == .claude {
            return Usage(fiveHour: claudeWindow(json["five_hour"]), weekly: claudeWindow(json["seven_day"]))
        }
        var result = Usage()
        guard let limits = json["rate_limit"] as? [String: Any] else { return result }
        for key in ["primary_window", "secondary_window"] {
            guard let window = limits[key] as? [String: Any],
                  let used = number(window["used_percent"]), used >= 0 else { continue }
            let value = UsageWindow(remaining: Int(floor(max(0, 100 - used))),
                                    resetsAt: timestamp(number(window["reset_at"])))
            switch number(window["limit_window_seconds"]) {
            case 18000: result.fiveHour = value
            case 604800: result.weekly = value
            default: break
            }
        }
        return result
    }

    private static func claudeWindow(_ raw: Any?) -> UsageWindow? {
        guard let window = raw as? [String: Any], let used = number(window["utilization"]),
              (0...100).contains(used) else { return nil }
        var reset: Date?
        if let text = window["resets_at"] as? String {
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            reset = formatter.date(from: text)
            if reset == nil {
                formatter.formatOptions = [.withInternetDateTime]
                reset = formatter.date(from: text)
            }
        }
        return UsageWindow(remaining: Int(floor(100 - used)),
                           resetsAt: timestamp(reset?.timeIntervalSince1970))
    }

    static func number(_ value: Any?) -> Double? {
        // JSON booleans bridge to NSNumber as well, but are never valid percentages.
        guard let value = value as? NSNumber, CFGetTypeID(value) != CFBooleanGetTypeID(),
              value.doubleValue.isFinite else { return nil }
        return value.doubleValue
    }

    static func timestamp(_ seconds: Double?) -> Date? {
        guard let seconds, seconds > 0, seconds <= 253_402_300_799 else { return nil }
        return Date(timeIntervalSince1970: seconds)
    }
}
