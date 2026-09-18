# Versioning

The product version has exactly two parts: `major.minor`.

- Increment `major` only for a significant new user-visible capability or a substantial change to how the application is used.
- Increment `minor` for bug fixes, small user-visible improvements, configuration changes, packaging changes, and other non-significant changes.
- Do not change the version for documentation-only changes that do not affect the shipped application.

Before every commit that changes the shipped application, determine the required increment, state the proposed version and its reason, and ask the user to confirm the selected `major` or `minor` increment. Do not create that commit until the user confirms.

## Distribution

- Keep installers out of Git. `build.ps1` produces the ignored `dist/AIUsageMonitor-Setup_v<major>.<minor>.exe`.
- Publish installers as GitHub Release assets named exactly `AIUsageMonitor-Setup_v<major>.<minor>.exe`. Update the README direct-download URL for the version being released in the same commit.
- A release tag `v<major>.<minor>` must point to the exact source commit used to build its installer. Publish only after version approval and the commit is pushed. Never overwrite an existing version's tag or asset.

## Documentation

- Update README.md in the same change whenever user-visible behavior, installation, configuration, or distribution changes. Keep installation steps and download details consistent with the shipped installer.
