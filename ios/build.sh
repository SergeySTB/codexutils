#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

if [[ "$(uname -s)" != Darwin ]]; then
  echo 'The iOS build requires macOS and Xcode. Use the manual iOS workflow from Windows.' >&2
  exit 1
fi
command -v xcodegen >/dev/null || { echo 'Install XcodeGen: brew install xcodegen' >&2; exit 1; }
xcodebuild -version
swift test --package-path UsageCore
xcodegen generate
# Simulator-only ad-hoc signing preserves the app's local entitlements without
# requiring an Apple development certificate or device provisioning profile.
simulator_signing=(CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- CODE_SIGN_STYLE=Manual)
xcodebuild -project AIUsageMonitor.xcodeproj -scheme AIUsageMonitor \
  -configuration Debug -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  -derivedDataPath build "${simulator_signing[@]}" build
codesign -d --entitlements :- build/Build/Products/Debug-iphonesimulator/AIUsageMonitor.app

if [[ -n "${IOS_TEST_DESTINATION:-}" ]]; then
  xcodebuild -project AIUsageMonitor.xcodeproj -scheme AIUsageMonitor \
    -configuration Debug -destination "$IOS_TEST_DESTINATION" \
    -derivedDataPath build -resultBundlePath build/UITests.xcresult "${simulator_signing[@]}" test
fi
