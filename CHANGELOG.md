# Changelog

All notable changes to Tray Translator are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/). The section for a version becomes its GitHub release notes.

## [Unreleased]

## [0.1.0] - Unreleased

First release.

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
- Replace button that pastes the translation over the selected text in the original window and then
  puts the user's clipboard back. Optional setting to replace automatically on Ctrl+C+C, with no popup.
- Tray menu with Settings, Pause hotkey, characters used this month, Check for updates, About and Quit.
- Settings window, and a first-run window that asks for the languages and tests a key.
- API keys stored encrypted with Windows DPAPI for the current user.
- Start with Windows, light and dark theme following Windows, a single running copy.
- Per-user installer with automatic updates from GitHub Releases.
