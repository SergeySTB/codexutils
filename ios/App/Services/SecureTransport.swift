import Foundation
import UsageCore

final class SecureTransport: NSObject, HTTPTransport, URLSessionTaskDelegate, @unchecked Sendable {
    private static let hosts: Set<String> = ["auth.openai.com", "chatgpt.com", "api.anthropic.com"]

    func send(_ original: URLRequest) async throws -> HTTPResponse {
        guard let url = original.url, url.scheme == "https", let host = url.host,
              Self.hosts.contains(host), url.user == nil, url.password == nil else { throw MonitorError.invalidResponse }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 20
        configuration.timeoutIntervalForResource = 30
        configuration.httpShouldSetCookies = false
        configuration.urlCache = nil
        let session = URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        var request = original
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("AIUsageMonitorIOS", forHTTPHeaderField: "User-Agent")
        let (bytes, response) = try await session.bytes(for: request)
        guard let response = response as? HTTPURLResponse else { throw MonitorError.invalidResponse }
        // Discard service error bodies; they may echo sensitive request information.
        guard (200..<300).contains(response.statusCode) else {
            return HTTPResponse(status: response.statusCode, data: Data())
        }
        var data = Data()
        for try await byte in bytes {
            guard data.count < 1_048_576 else { throw MonitorError.invalidResponse }
            data.append(byte)
        }
        return HTTPResponse(status: response.statusCode, data: data)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}
