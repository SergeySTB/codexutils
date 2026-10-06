import SwiftUI
import UIKit
import UsageCore

struct ConnectionView: View {
    let route: ConnectionRoute
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL
    @State private var name = ""
    @State private var token = ""
    @State private var device: DeviceCode?
    @State private var issue: MonitorError?
    @State private var saving = false
    @State private var attempt = 0

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Label(route.provider.title, systemImage: "person.crop.circle.badge.plus")
                        .font(.title2.weight(.semibold)).padding(.vertical, 8)
                    if let account = route.account {
                        Text(account.name).font(.headline)
                        Text(L10n.text("renew_hint")).foregroundStyle(.secondary)
                    }
                }
                if route.provider == .claude { claudeForm } else { codexForm }
                if let issue {
                    Section { Label(L10n.text("error_" + issue.rawValue), systemImage: "exclamationmark.triangle").foregroundStyle(.orange) }
                }
                Section { Text(L10n.text("privacy_hint")).font(.footnote).foregroundStyle(.secondary) }
            }
            .navigationTitle(L10n.text(route.account == nil ? "add_account" : "renew"))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button(L10n.text("cancel")) { dismiss() }.disabled(saving) }
            }
            .interactiveDismissDisabled(saving)
            .onAppear { name = route.account?.name ?? "" }
            .task(id: attempt) { if route.provider == .codex { await connectCodex() } }
        }.environment(\.locale, SharedSettings.locale)
    }

    private var claudeForm: some View {
        Section {
            TextField(L10n.text("account_name"), text: $name).textContentType(.nickname)
            SecureField(L10n.text("oauth_token"), text: $token)
                .textInputAutocapitalization(.never).autocorrectionDisabled()
            Text(L10n.text("claude_hint")).font(.footnote).foregroundStyle(.secondary)
            Button {
                saving = true
                Task {
                    defer { saving = false }
                    do {
                        try await AccountRepository.shared.saveClaude(name: name, token: token, replacing: route.account?.id)
                        token = ""
                        dismiss()
                    } catch { issue = MonitorError.from(error) }
                }
            } label: {
                HStack { Text(L10n.text("save")); if saving { Spacer(); ProgressView() } }
            }.disabled(saving || name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || name.count > 60 ||
                !TokenIdentity.validClaudeToken(token.trimmingCharacters(in: .whitespacesAndNewlines)))
        }
    }

    private var codexForm: some View {
        Section {
            if let device {
                Text(L10n.text("device_hint"))
                Text(device.code).font(.largeTitle.monospaced().weight(.bold)).textSelection(.enabled)
                    .minimumScaleFactor(0.7).accessibilityLabel(L10n.text("device_code") + ": " + device.code)
                Button(L10n.text("copy_open"), systemImage: "arrow.up.right.square") {
                    UIPasteboard.general.setItems([[UIPasteboard.typeAutomatic: device.code]], options: [
                        .localOnly: true, .expirationDate: device.expiresAt
                    ])
                    openURL(URL(string: "https://auth.openai.com/codex/device")!)
                }
                if issue == nil { ProgressView(L10n.text("waiting_approval")) }
            } else if issue == nil { ProgressView(L10n.text("getting_code")) }
            if issue != nil { Button(L10n.text("retry")) { attempt += 1 } }
        }
    }

    private func connectCodex() async {
        issue = nil
        device = nil
        let client = ProviderClient(transport: SecureTransport())
        do {
            let code = try await client.beginLogin()
            device = code
            let account = try await client.awaitApproval(code)
            try Task.checkCancellation()
            saving = true
            defer { saving = false }
            try await AccountRepository.shared.saveCodex(account, replacing: route.account?.id)
            dismiss()
        } catch is CancellationError { }
        catch { if !Task.isCancelled { issue = MonitorError.from(error) } }
    }
}
