# AI Usage Monitor for iOS

Native, standalone iPhone and iPad source project (iOS 17+). No Windows companion
or hosted backend is required. This is an **unreleased implementation**:
simulator compilation has succeeded in CI, while device sign-in and background
refresh remain unverified. Complete the Mac/CI checks below before distributing
a build.

## Features

- Multiple Codex and Claude accounts; Codex device-code sign-in and refresh-token
  rotation; Claude OAuth-token entry and replacement using `claude setup-token`.
- Account-specific reconnection checks the Codex account identity before replacing
  credentials. Removing an account removes its credentials and cached reading.
- Remaining 5-hour and weekly limits, reset dates, plan and last successful update.
  Missing windows stay absent; errors preserve the previous reading and mark it stale.
- Native SwiftUI navigation, sheets, menus and tab bar. Building with Xcode 26+
  adopts the system's Liquid Glass navigation on supporting iOS versions, with
  standard native presentation on iOS 17/18. Content uses solid adaptive surfaces
  for legibility, teal/purple limit rings, existing product graphics, Dynamic Type,
  VoiceOver labels and light/dark appearance. No custom motion is required.
- Home Screen icon and card widgets, plus a circular Lock Screen widget for the
  first account's 5-hour allowance. Small widgets show one account; medium icon
  widgets show up to three; large card widgets show up to three. Overflow is `+N`.
- Persisted System / English / Russian language selection, widget previews and
  requested background intervals from 15 minutes to 24 hours (default 30 minutes).

The two provider routes match the Android implementation. They are not guaranteed
public third-party APIs; live compatibility still requires device testing.

## Build without owning a Mac

The repository includes the manual **iOS build and checks** GitHub Actions workflow
(`.github/workflows/ios-check.yml`). Once these sources are deliberately pushed to
GitHub, open **Actions → iOS build and checks → Run workflow** from any computer.
This uses a macOS runner to test `UsageCore`, build the app and widget extension,
and run a simulator navigation test. It saves a simulator `.app` ZIP and UI test
results as workflow artifacts. It does not sign an iPhone build, create a release,
upload to TestFlight, or run automatically on push. Runner usage is subject to
your GitHub plan; no remote run is necessary for editing the sources.

**A simulator ZIP cannot be installed on an iPhone or run on Windows.** A native
iOS build still requires Apple's SDK/Xcode on a local or remote Mac. For delivery
to a physical device, arrange Apple signing and provisioning; TestFlight is a
practical Windows-accessible installation route after a signed upload from macOS.
Do not place certificates, provisioning profiles or Apple credentials in Git.

## Local build on macOS

Install Xcode 26 or later, select its command-line tools, install an iOS simulator
runtime in Xcode, and install [XcodeGen](https://github.com/yonaskolb/XcodeGen):

```sh
brew install xcodegen
bash ios/build.sh
```

The script runs the pure Swift package tests, generates the Xcode project from
`project.yml`, then requests local ad-hoc signing for the simulator. This
ad-hoc signature needs no Apple development certificate or device provisioning
profile, and cannot be used to install the app on an iPhone.
Generated files and build outputs are ignored. Asset catalogs are already present;
to recreate them using the existing repository artwork, run
`python3 ios/scripts/make_assets.py` from the repository root.

To run the UI smoke test as well, choose an installed simulator:

```sh
xcrun simctl list devices available
IOS_TEST_DESTINATION='platform=iOS Simulator,id=<simulator-UUID>' bash ios/build.sh
open ios/AIUsageMonitor.xcodeproj
```

Select **AIUsageMonitor** and a simulator, then Run. `UsageCore` can also be tested
independently with `swift test --package-path ios/UsageCore` on a supported Swift host.
It has no third-party package dependencies.

## Device signing

1. Set `APP_BUNDLE_ID` to your registered identifier and `APP_GROUP_ID` to your
   registered App Group in the generated project's build settings (or locally in
   `project.yml`). The app and extension must use the same App Group.
2. Select your Apple development team for both **AIUsageMonitor** and
   **UsageWidgets**. Provision both identifiers, including App Groups capability.
   The widget identifier is the application identifier plus `.widgets`.
3. Build and run on a registered iPhone/iPad, or archive for distribution and use
   Xcode's signing/export workflow. App Groups require a provisioning configuration
   that supports that capability; do not assume a free Personal Team can provision
   the complete app plus extension.
4. For remote signed builds, configure your CI's protected signing secrets only
   after choosing a distribution method. The supplied workflow intentionally
   verifies the simulator build without requiring any Apple credentials.

The existing product version is retained during local development. A new platform
is a major release candidate; obtain the repository-required version approval
before committing a shipped version. Existing Windows/Android download URLs and
release assets are unaffected.

## Background updates and privacy

The app refreshes Codex at most once per minute and Claude at most once per five
minutes. Both providers wait at least 15 minutes after HTTP 429, including manual
refresh attempts; the deadlines persist across restarts. Polling stops when the
app is inactive. Cancellable `BGAppRefreshTask` work requests background updates.
iOS can delay or skip them, especially under battery restrictions, when Background
App Refresh is disabled or after force-quitting the app. Reopen the app to resume.

The widget extension reads cached summaries and never authenticates or rotates
tokens. The application publishes snapshots and reloads widget timelines after
updates. Timeline ticks can update display age but **do not fetch usage**; the
requested interval is not a guarantee of fresh network data. This is an explicit
iOS adaptation of Android's background job behavior.

Credentials and account records use `AfterFirstUnlockThisDeviceOnly` Keychain
protection: no iCloud synchronization or backup migration. Widgets receive only
account names, plans, percentages, timestamps and safe error identifiers through
an App Group file with data protection and backup exclusion. They are marked
privacy-sensitive. Requests use ephemeral URL sessions, HTTPS host allowlists,
bounded response bodies and no redirects. No raw provider errors or tokens are
logged or included in alerts. Removing all accounts leaves an empty Keychain
record, and deleting the app is not a substitute for explicit account removal
because iOS may retain Keychain entries after uninstall.

## Verification checklist

The core test suite covers duration-based window selection, missing/model quotas,
invalid percentages and dates, token validation, account-identity mismatches,
rotation, request headers, 429 mapping, login cancellation, snapshot credential
exclusion and staleness. The UI test covers tabs, Claude account entry and cancel.

Before release, also verify on an actual device:

- Codex login/approval/cancel, token renewal, rejection of another account during
  reconnection; multiple accounts and deletion during normal operation.
- Claude entry/renewal, expired credentials, offline recovery and rate limiting.
- Keychain access after restart/unlock, App Group entitlements, widget removal,
  background refresh with the app suspended and after a force-quit/reopen.
- English/Russian switching, VoiceOver, largest Dynamic Type, Reduce Transparency,
  dark/light/tinted widgets, compact iPhone and iPad landscape layouts.
- Signed archive/export and privacy declarations for the selected distribution.

## Platform references

- [Apple: adopting Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass)
- [Apple: keeping a widget up to date](https://developer.apple.com/documentation/widgetkit/keeping-a-widget-up-to-date)
- [GitHub: hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
- [Apple: distributing your app](https://developer.apple.com/documentation/xcode/distributing-your-app-for-beta-testing-and-releases)
