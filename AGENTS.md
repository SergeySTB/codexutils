# Versioning

The product version has exactly two parts: `major.minor`.

- Increment `major` only for a significant new user-visible capability or a substantial change to how the application is used.
- Increment `minor` for bug fixes, small user-visible improvements, configuration changes, packaging changes, and other non-significant changes.
- Do not change the version for documentation-only changes that do not affect the shipped application.

Before every commit that changes the shipped application, determine the required increment, state the proposed version and its reason, and ask the user to confirm the selected `major` or `minor` increment. Do not create that commit until the user confirms.

## Distribution

- Keep installers out of Git. `build.ps1` produces the ignored `dist/CodexLimits-Setup.exe`.
- Publish installers as GitHub Release assets named exactly `CodexLimits-Setup.exe`; the README uses the latest-release direct-download URL.
- A release tag `v<major>.<minor>` must point to the exact source commit used to build its installer. Publish only after version approval and the commit is pushed. Never overwrite an existing version's tag or asset.
