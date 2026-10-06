import XCTest
@testable import UsageCore
import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

final class UsageCoreTests: XCTestCase {
    private func parse(_ json: String, _ provider: Provider = .codex) throws -> Usage {
        try UsageParser.parse(Data(json.utf8), provider: provider)
    }

    func testWindowDurationDeterminesMeaningRatherThanPosition() throws {
        let usage = try parse(#"{"rate_limit":{"primary_window":{"used_percent":25.5,"limit_window_seconds":604800},"secondary_window":{"used_percent":80,"limit_window_seconds":18000}}}"#)
        XCTAssertEqual(usage.fiveHour?.remaining, 20)
        XCTAssertEqual(usage.weekly?.remaining, 74)
    }

    func testAbsentMalformedAndModelLimitsAreNotZeroOrFallback() throws {
        let usage = try parse(#"{"rate_limit":{"primary_window":{"used_percent":true,"limit_window_seconds":18000},"secondary_window":{"used_percent":-1,"limit_window_seconds":604800}},"additional_rate_limits":[{"rate_limit":{"primary_window":{"used_percent":40,"limit_window_seconds":18000}}}]}"#)
        XCTAssertNil(usage.fiveHour)
        XCTAssertNil(usage.weekly)
        XCTAssertEqual(try parse("{}"), Usage())
        XCTAssertEqual(try parse(#"{"rate_limit":{"primary_window":{"used_percent":20,"limit_window_seconds":2592000}}}"#), Usage())
    }

    func testCodexClampsOverageAndRejectsInvalidReset() throws {
        let usage = try parse(#"{"rate_limit":{"primary_window":{"used_percent":150,"limit_window_seconds":18000,"reset_at":-2}}}"#)
        XCTAssertEqual(usage.fiveHour?.remaining, 0)
        XCTAssertNil(usage.fiveHour?.resetsAt)
    }

    func testClaudePercentagesAndFractionalISODate() throws {
        let usage = try parse(#"{"five_hour":{"utilization":25.5,"resets_at":"2026-10-05T10:00:00.123Z"},"seven_day":{"utilization":80}}"#, .claude)
        XCTAssertEqual(usage.fiveHour?.remaining, 74)
        XCTAssertNotNil(usage.fiveHour?.resetsAt)
        XCTAssertEqual(usage.weekly?.remaining, 20)
        XCTAssertNil(usage.weekly?.resetsAt)
    }

    func testClaudeRejectsOutOfRangeAndBoolean() throws {
        let usage = try parse(#"{"five_hour":{"utilization":101},"seven_day":{"utilization":true}}"#, .claude)
        XCTAssertEqual(usage, Usage())
    }

    func testClaudeTokenValidation() {
        XCTAssertTrue(TokenIdentity.validClaudeToken("sk-ant-oat01-example_123"))
        for token in ["", "sk-ant-api01-example", "sk-ant-oat01-abc\nInjected: x", String(repeating: "x", count: 4097)] {
            XCTAssertFalse(TokenIdentity.validClaudeToken(token))
        }
    }

    func testTokenIdentityRejectsMismatchedClaims() throws {
        XCTAssertThrowsError(try TokenIdentity.read(idToken: token(account: "one"), accessToken: token(account: "two"))) {
            XCTAssertEqual($0 as? MonitorError, .accountMismatch)
        }
    }

    func testTokenIdentityAcceptsAccessFallbackAndOpaqueAccess() throws {
        let id = token(account: "", email: "person@example.test")
        XCTAssertEqual(try TokenIdentity.read(idToken: id, accessToken: token(account: "one")).accountID, "one")
        XCTAssertEqual(try TokenIdentity.read(idToken: token(account: "one"), accessToken: "opaque").accountID, "one")
        XCTAssertThrowsError(try TokenIdentity.read(idToken: "broken", accessToken: "opaque"))
    }

    func testRefreshPolicyAndSettingsNormalization() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        XCTAssertEqual(RefreshPolicy.deadline(provider: .claude, issue: nil, now: now).timeIntervalSince(now), 300)
        XCTAssertEqual(RefreshPolicy.deadline(provider: .codex, issue: .rateLimited, now: now).timeIntervalSince(now), 900)
        XCTAssertEqual(RefreshPolicy.normalized(0), 30)
        XCTAssertEqual(RefreshPolicy.normalized(1440), 1440)
    }

    func testWidgetSnapshotHasNoCredentialsAndPreservesUnavailableWindows() throws {
        var account = AccountSummary(provider: .claude, name: "Personal")
        account.usage = Usage(weekly: UsageWindow(remaining: 80))
        let data = try JSONEncoder().encode(account)
        let json = String(decoding: data, as: UTF8.self)
        for field in ["accessToken", "refreshToken", "idToken", "accountID"] { XCTAssertFalse(json.contains(field)) }
        XCTAssertEqual(try JSONDecoder().decode(AccountSummary.self, from: data), account)
    }

    func testStalenessUsesSavedTimestampAndFailure() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        var account = AccountSummary(provider: .codex, name: "Personal")
        XCTAssertTrue(account.isStale(at: now))
        account.updatedAt = now
        XCTAssertFalse(account.isStale(at: now))
        XCTAssertTrue(account.isStale(at: now.addingTimeInterval(3601)))
        account.issue = .network
        XCTAssertTrue(account.isStale(at: now))
    }

    func testClaudeRequestHeadersAndRateLimitMapping() async throws {
        let transport = StubTransport([HTTPResponse(status: 429, data: Data())])
        let client = ProviderClient(transport: transport)
        do {
            _ = try await client.usage(provider: .claude, credentials: Credentials(accessToken: "sk-ant-oat01-test"))
            XCTFail("Expected rate limit")
        } catch { XCTAssertEqual(error as? MonitorError, .rateLimited) }
        let request = await transport.requests.first
        XCTAssertEqual(request?.url?.host, "api.anthropic.com")
        XCTAssertEqual(request?.value(forHTTPHeaderField: "anthropic-beta"), "oauth-2025-04-20")
        XCTAssertNil(request?.value(forHTTPHeaderField: "ChatGPT-Account-Id"))
    }

    func testRefreshRejectsAccountChange() async throws {
        let data = try JSONSerialization.data(withJSONObject: ["access_token": token(account: "other"),
            "id_token": token(account: "other"), "refresh_token": "new-refresh"])
        let client = ProviderClient(transport: StubTransport([HTTPResponse(status: 200, data: data)]))
        do {
            _ = try await client.refresh(Credentials(accessToken: "old", refreshToken: "old-refresh", accountID: "original"))
            XCTFail("Expected identity rejection")
        } catch { XCTAssertEqual(error as? MonitorError, .accountMismatch) }
    }

    func testRotatedTokensAndAbsentOptionalRefreshFields() async throws {
        let data = try JSONSerialization.data(withJSONObject: ["access_token": token(account: "one")])
        let client = ProviderClient(transport: StubTransport([HTTPResponse(status: 200, data: data)]))
        let previous = Credentials(accessToken: "old", refreshToken: "retain-refresh", idToken: token(account: "one"), accountID: "one")
        let updated = try await client.refresh(previous)
        XCTAssertEqual(updated.credentials.refreshToken, "retain-refresh")
        XCTAssertEqual(updated.credentials.idToken, previous.idToken)
        XCTAssertNotEqual(updated.credentials.accessToken, previous.accessToken)
    }

    func testDeviceCodeParsingAndImmediateApproval() async throws {
        let issued = Data(#"{"device_auth_id":"test-id","user_code":"ABCD-EFGH","interval":"1"}"#.utf8)
        let approval = Data(#"{"authorization_code":"one+time","code_verifier":"verifier"}"#.utf8)
        let tokens = try JSONSerialization.data(withJSONObject: ["access_token": token(account: "one"),
            "id_token": token(account: "one"), "refresh_token": "refresh"])
        let transport = StubTransport([issued, approval, tokens].map { HTTPResponse(status: 200, data: $0) })
        let client = ProviderClient(transport: transport)
        let device = try await client.beginLogin()
        XCTAssertEqual(device.interval, 5)
        let signedIn = try await client.awaitApproval(device)
        XCTAssertEqual(signedIn.identity.accountID, "one")
        let requests = await transport.requests
        XCTAssertEqual(requests.count, 3)
        XCTAssertTrue(String(decoding: requests[2].httpBody!, as: UTF8.self).contains("one%2Btime"))
    }

    func testCancelledLoginMakesNoPollingRequest() async throws {
        let transport = StubTransport([HTTPResponse(status: 200, data: Data(#"{"device_auth_id":"id","user_code":"code"}"#.utf8))])
        let client = ProviderClient(transport: transport)
        let device = try await client.beginLogin()
        let task = Task {
            withUnsafeCurrentTask { $0?.cancel() }
            return try await client.awaitApproval(device)
        }
        do { _ = try await task.value; XCTFail("Expected cancellation") }
        catch { XCTAssertTrue(error is CancellationError) }
        let count = await transport.requests.count
        XCTAssertEqual(count, 1)
    }

    private func token(account: String, email: String = "person@example.test") -> String {
        let json: [String: Any] = ["email": email, "exp": 2_000_000_000,
            "https://api.openai.com/auth": ["chatgpt_account_id": account, "chatgpt_plan_type": "plus"]]
        let data = try! JSONSerialization.data(withJSONObject: json)
        return "header." + data.base64EncodedString().replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "") + ".signature"
    }
}

private actor StubTransport: HTTPTransport {
    private var responses: [HTTPResponse]
    var requests: [URLRequest] = []
    init(_ responses: [HTTPResponse]) { self.responses = responses }
    func send(_ request: URLRequest) async throws -> HTTPResponse {
        requests.append(request)
        guard !responses.isEmpty else { throw MonitorError.network }
        return responses.removeFirst()
    }
}
