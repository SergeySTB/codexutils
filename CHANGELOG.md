# Changelog

This file lists notable user-facing changes to AI Usage Monitor.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [6.8] — 2026-09-30

### Added

- Windows: the installer's first screen now includes a startup checkbox for all
  users. Clearing it disables startup during an update; uninstalling the app
  removes the startup entry.

## [6.7] — 2026-09-28

### Fixed

- Android: icon rings use the maximum available width and height. Two accounts
  fit in two cells; expanding the widget allows three accounts in three cells
  or four in four cells.

## [6.6] — 2026-09-28

### Added

- Account cards show a sign-in recovery button after an authentication error.
  Windows starts browser sign-in for Codex or Claude Code for the selected
  profile folder; Android signs the selected Codex account in again or updates
  the Claude token on its card.

### Fixed

- Android: reduced icon widget ring sizes so two accounts fit within two home
  screen cells.

## [6.1] — 2026-09-23

### Added

- The Windows panel and tray menus allow adding a Codex or Claude account with
  a name and profile folder, and removing an account from the app. Removing an
  account leaves its sign-in data in the profile folder on disk.
- An empty Windows panel shows an add-account button.

### Fixed

- The Windows installer preserves an empty account list during updates.

## [6.0] — 2026-09-23

### Added

- Shared Windows and Android version: `6.0`. Both apps track Claude Code's
  5-hour and weekly limits alongside Codex.
- Icons show the OpenAI or Claude logo and account initial inside the existing
  usage limit rings; cards explicitly name the provider.
- Windows reads sign-in data from a Claude Code profile. Android accepts a
  Claude OAuth token and stores it in encrypted phone storage.

## [5.3] — 2026-09-22

### Added

- Android 5.2: main screen actions moved to the top-right menu; account removal
  is also selected there and still requires confirmation. Enlarged widget icons,
  added a hidden-account counter and previews of both widgets in the add-widget
  menu; the empty screen indicates where to add an account.
- Android 5.1: a settings screen for choosing the background widget refresh
  interval from 15 minutes to 24 hours, with a default of 30 minutes.
- Android 5.0: two home screen widgets — a row of icons with usage limit arcs
  and scrollable account cards. Limits are saved for widgets and refreshed in
  the background approximately every 30 minutes, subject to Android restrictions.

### Fixed

- Android 5.3: indicators in the icon widget adapt to its height; the background
  around the circles is translucent, while the inside of the circles is opaque.

## [4.4] — 2026-09-22

### Added

- Initial standalone Android app project with device code sign-in, multiple
  accounts and a view of Codex's 5-hour and weekly limits.

### Fixed

- Android sign-in can obtain the account identifier from the access token if
  it is missing from the ID token; failures show the stage instead of a generic message.
- Android sign-in distinguishes failures while waiting for confirmation from
  failures during token exchange; short polling requests do not use streaming upload mode.
- Android sign-in continues after a temporary DNS error while waiting for code
  confirmation: polling continues until the code expires.
- The Android app displays limits from the main Codex response, including when
  the service returns only the weekly window.

## [3.2] — 2026-09-21

### Added

- Dragging the icon panel and cards with the mouse saves the monitor and position
  in the configuration for the next launch.

### Fixed

- Changing display mode through the menu preserves comments and correctly adds
  a missing `displayMode` setting to the configuration.

## [3.1] — 2026-09-18

### Added

- Switching between icons and cards in the widget and tray icon menus.

### Fixed

- Cards accept compact sizes, including 200×200 pixels.

## [3.0] — 2026-09-18

### Changed

- The user-facing name of the app, installer, Start menu entry and installed
  programs entry changed to AI Usage Monitor.
- Source and test directories, solution, namespace, EXE and internal identifiers
  renamed to AIUsageMonitor. Existing configuration and profile paths continue
  to be used during updates.

### Fixed

- Removed technical details from the installation completion window; its title
  contains the app name and version.

## [2.0] — 2026-09-18

### Added

- Always-visible cards for all accounts: a row along the top/bottom edge or a
  column along the left/right edge, selected with `widget.displayMode`.
- Independent card dimensions through `cardWidthPx` and `cardHeightPx`, automatic
  sizing when set to `0`, and scrolling when space is insufficient.

### Changed

- Compact panel dimensions renamed to `iconWidthPx` and `iconHeightPx` and apply
  only in icon mode. Old settings are migrated automatically.

### Fixed

- The installer starts without CMD or a PowerShell console. The completion
  window no longer hides with the console, leaving installation waiting for
  an invisible button.

## [1.3] — 2026-09-18

### Added

- Account count is determined by the `accounts` array; one account is created
  by default, and an English configuration comment contains a second-account example.
- Installation completion window with status, error details and a checkbox to
  launch the app after successful installation.

### Fixed

- Installation, configuration updates and uninstallation run without visible terminals.
- The installer closes the running widget and waits for it to exit before
  updating files; installation stops with an error if this fails.
- Waiting for installation to finish no longer includes the running time of
  the widget launched after installation.

## [1.2] — 2026-09-18

### Fixed

- The installer installs the app in `Program Files`, registers it in the
  installed programs list and provides uninstallation through standard Windows tools.
- Configuration updates preserve previous settings and a backup more reliably.
- The installer finds `codex.exe` in the Codex Desktop installation, PATH or
  global npm package and writes its path to an empty `codexExecutable` field.

## [1.1] — 2026-09-17

### Added

- App and installer icon.
- Automatic installer builds and artifact publication in GitHub Releases.

### Changed

- Source code, tests, configuration and packaging scripts organized into the
  standard `src`, `tests`, `config` and `packaging` directories.

## [1.0] — 2026-09-17

### Added

- Windows 11 widget for two Codex accounts with 5-hour and weekly limit indicators.
- Widget size, position and margin settings through JSON configuration.
- Account detail popups and manual refresh.
- Optional sound notification when a limit resets.
- Existing configuration migration during installation with a backup of the original file.

[Unreleased]: https://github.com/SergeySTB/codexutils/compare/v6.1...HEAD
[6.1]: https://github.com/SergeySTB/codexutils/releases/tag/v6.1
[6.0]: https://github.com/SergeySTB/codexutils/releases/tag/v6.0
[5.3]: https://github.com/SergeySTB/codexutils/releases/tag/v5.3
[4.4]: https://github.com/SergeySTB/codexutils/releases/tag/v4.4
[3.0]: https://github.com/SergeySTB/codexutils/compare/v2.0...v3.0
[2.0]: https://github.com/SergeySTB/codexutils/compare/v1.3...v2.0
[1.3]: https://github.com/SergeySTB/codexutils/compare/v1.2...v1.3
[1.2]: https://github.com/SergeySTB/codexutils/compare/v1.1...v1.2
[1.1]: https://github.com/SergeySTB/codexutils/compare/v1.0...v1.1
[1.0]: https://github.com/SergeySTB/codexutils/releases/tag/v1.0
