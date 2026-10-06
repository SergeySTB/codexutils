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
xcodebuild -project AIUsageMonitor.xcodeproj -scheme AIUsageMonitor \
  -configuration Debug -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  -derivedDataPath build CODE_SIGNING_ALLOWED=NO build

if [[ -n "${IOS_TEST_DESTINATION:-}" ]]; then
  xcodebuild -project AIUsageMonitor.xcodeproj -scheme AIUsageMonitor \
    -configuration Debug -destination "$IOS_TEST_DESTINATION" \
    -derivedDataPath build -resultBundlePath build/UITests.xcresult CODE_SIGNING_ALLOWED=NO test
fi
