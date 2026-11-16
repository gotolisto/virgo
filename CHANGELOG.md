# Changelog

Every version of Tray Translator and what it changed, newest first. The app shows this file in its
**What's new** window, and the section for a version becomes its GitHub release notes.

Versions follow [semantic versioning](https://semver.org/) (MAJOR.MINOR.PATCH): new features raise MINOR
(0.2.0 → 0.3.0), fixes on their own raise PATCH (0.2.0 → 0.2.1). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

**Rule for every change:** add a line under *Unreleased* in the same pull request. To release, see
"Versions and releasing" in the README.

## [Unreleased]

## [0.2.0] - 2026-11-16

The first published release.

### Added
- **Replace** button in the popup: swaps the selected text in the original window for the translation,
  then puts your copied text back on the clipboard.
- Setting to replace automatically on Ctrl+C+C, with no popup. The popup still appears for errors, or
  when the window does not accept the paste.
- Language menu in the popup header: translate the same text into Language A, Language B or any extra
  language ticked in Settings. The pick applies to that popup only.
- **What's new** window, from the tray menu and About, listing every version and its changes. After an
  update, a notification links to it.

### Fixed
- The Windows release build, which stopped at package restore with a newer .NET SDK.

## [0.1.0] - 2026-11-15

The first build, not published as a release.

### Added
- Translate the selected text with Ctrl+C+C in any app. The popup opens next to the cursor, stays inside
  the screen, and closes on Esc or when it loses focus.
- Two languages, English and Spanish by default. Text in either is translated into the other; anything
  else goes into the first language. Detection runs locally.
- DeepL API Free and Google Cloud Translation, with your own keys. When one runs out of its monthly
  allowance the other takes over, and the popup says so once.
- Google is stopped at 480,000 characters a month by default, because Google bills past its free
  allowance. "Allow paid usage" lifts the limit.
- Popup with a swap button, Copy, and clear messages for no connection, a bad key, rate limits and
  exhausted allowances.
- Tray menu with Settings, Pause hotkey, characters used this month, Check for updates, About and Quit.
- Settings window, and a first-run window that asks for the languages and tests a key.
- API keys stored encrypted with Windows DPAPI for the current user.
- Start with Windows, light and dark theme following Windows, a single running copy.
- Per-user installer with automatic updates from GitHub Releases.
