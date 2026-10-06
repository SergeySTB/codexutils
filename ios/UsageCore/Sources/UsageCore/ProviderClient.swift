import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public struct HTTPResponse: Sendable {
    public let status: Int
    public let data: Data
    public init(status: Int, data: Data) { self.status = status; self.data = data }
}

public protocol HTTPTransport: Sendable {
    func send(_ request: URLRequest) async throws -> HTTPResponse
}

public struct DeviceCode: Sendable {
    public let id: String
    public let code: String
    public let interval: TimeInterval
    public let expiresAt: Date
}

public struct SignedInAccount: Sendable {
    public let identity: TokenIdentity
    public let credentials: Credentials
}

public struct ProviderClient: Sendable {
    private let transport: any HTTPTransport
    private let auth = "https://auth.openai.com"
    private let clientID = "app_EMoamEEZ73f0CkXaXp7hrann"
    public init(transport: any HTTPTransport) { self.transport = transport }

    public func beginLogin(now: Date = Date()) async throws -> DeviceCode {
        let data = try await jsonRequest(path: "/api/accounts/deviceauth/usercode", body: ["client_id": clientID])
        guard let id = data["device_auth_id"] as? String, !id.isEmpty, id.count <= 4096,
              let code = (data["user_code"] ?? data["usercode"]) as? String, !code.isEmpty, code.count <= 128 else {
            throw MonitorError.invalidResponse
        }
        let interval = (data["interval"] as? String).flatMap(Double.init) ?? UsageParser.number(data["interval"]) ?? 5
        guard interval.isFinite else { throw MonitorError.invalidResponse }
        return DeviceCode(id: id, code: code, interval: max(5, min(30, interval)), expiresAt: now.addingTimeInterval(900))
    }

    public func awaitApproval(_ device: DeviceCode) async throws -> SignedInAccount {
        while Date() < device.expiresAt {
            try Task.checkCancellation()
            let request = try makeRequest(auth + "/api/accounts/deviceauth/token", body: [
                "device_auth_id": device.id, "user_code": device.code
            ])
            let response = try await transport.send(request)
            if response.status == 200 {
                let code = try object(response.data)
                guard let authorization = code["authorization_code"] as? String, !authorization.isEmpty,
                      let verifier = code["code_verifier"] as? String, !verifier.isEmpty else { throw MonitorError.invalidResponse }
                var components = URLComponents()
                components.queryItems = [
                    URLQueryItem(name: "grant_type", value: "authorization_code"),
                    URLQueryItem(name: "client_id", value: clientID),
                    URLQueryItem(name: "code", value: authorization),
                    URLQueryItem(name: "redirect_uri", value: auth + "/deviceauth/callback"),
                    URLQueryItem(name: "code_verifier", value: verifier)
                ]
                var exchange = URLRequest(url: URL(string: auth + "/oauth/token")!)
                exchange.httpMethod = "POST"
                exchange.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
                exchange.httpBody = components.percentEncodedQuery?.replacingOccurrences(of: "+", with: "%2B").data(using: .utf8)
                let tokens = try await transport.send(exchange)
                try requireSuccess(tokens.status)
                try Task.checkCancellation()
                return try signedIn(object(tokens.data))
            }
            if response.status != 403 && response.status != 404 { try requireSuccess(response.status) }
            try await Task.sleep(nanoseconds: UInt64(device.interval * 1_000_000_000))
        }
        throw MonitorError.timedOut
    }

    public func refresh(_ previous: Credentials) async throws -> SignedInAccount {
        guard !previous.refreshToken.isEmpty else { throw MonitorError.signInRequired }
        let body = try await jsonRequest(path: "/oauth/token", body: [
            "grant_type": "refresh_token", "client_id": clientID, "refresh_token": previous.refreshToken
        ])
        let updated = try signedIn(body, previous: previous)
        guard updated.identity.accountID == previous.accountID else { throw MonitorError.accountMismatch }
        return updated
    }

    public func usage(provider: Provider, credentials: Credentials) async throws -> Usage {
        guard !credentials.accessToken.isEmpty else { throw MonitorError.signInRequired }
        let address = provider == .codex ? "https://chatgpt.com/backend-api/wham/usage" : "https://api.anthropic.com/api/oauth/usage"
        var request = URLRequest(url: URL(string: address)!)
        request.setValue("Bearer " + credentials.accessToken, forHTTPHeaderField: "Authorization")
        if provider == .codex {
            guard !credentials.accountID.isEmpty else { throw MonitorError.invalidToken }
            request.setValue(credentials.accountID, forHTTPHeaderField: "ChatGPT-Account-Id")
        } else {
            guard TokenIdentity.validClaudeToken(credentials.accessToken) else { throw MonitorError.invalidToken }
            request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        }
        let response = try await transport.send(request)
        try requireSuccess(response.status)
        return try UsageParser.parse(response.data, provider: provider)
    }

    private func signedIn(_ tokens: [String: Any], previous: Credentials? = nil) throws -> SignedInAccount {
        guard let access = tokens["access_token"] as? String, !access.isEmpty else { throw MonitorError.invalidResponse }
        let refresh = tokens["refresh_token"] as? String ?? previous?.refreshToken ?? ""
        let id = tokens["id_token"] as? String ?? previous?.idToken ?? ""
        guard !refresh.isEmpty else { throw MonitorError.invalidResponse }
        let identity = try TokenIdentity.read(idToken: id, accessToken: access)
        return SignedInAccount(identity: identity, credentials: Credentials(accessToken: access, refreshToken: refresh,
            idToken: id, accountID: identity.accountID, expiresAt: identity.expiresAt))
    }

    private func jsonRequest(path: String, body: [String: String]) async throws -> [String: Any] {
        let response = try await transport.send(makeRequest(auth + path, body: body))
        if path == "/oauth/token", response.status == 400 { throw MonitorError.signInRequired }
        try requireSuccess(response.status)
        return try object(response.data)
    }

    private func makeRequest(_ address: String, body: [String: String]) throws -> URLRequest {
        var request = URLRequest(url: URL(string: address)!)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        return request
    }

    private func object(_ data: Data) throws -> [String: Any] {
        guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw MonitorError.invalidResponse
        }
        return object
    }

    private func requireSuccess(_ status: Int) throws {
        switch status {
        case 200..<300: return
        case 401: throw MonitorError.signInRequired
        case 403: throw MonitorError.forbidden
        case 429: throw MonitorError.rateLimited
        default: throw MonitorError.network
        }
    }
}
