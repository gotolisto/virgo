# Tray Translator

A small Windows 10/11 tray app. Select text in any program, hold **Ctrl** and press **C** twice, and the
translation appears in a popup next to the cursor.

- Works in any app that can copy text: browsers, Notepad, Word, Outlook, PDF readers.
- Translates between your two languages (English ⇄ Spanish by default). Text in any other language is
  translated into your first language.
- Uses **DeepL API Free** and **Google Cloud Translation**, with your own keys. When one runs out of
  its monthly allowance, the other takes over.
- Leaves your clipboard alone unless you press **Copy** in the popup.
- No history, no telemetry. The selected text goes only to the engine in use, over HTTPS.

## Using it

| Action | How |
| --- | --- |
| Translate | Select text, hold Ctrl, press C twice quickly (within 0.5 s by default) |
| Translate the other way | The ⇄ button in the popup header |
| Copy the translation | **Copy** (closes the popup) |
| Put the translation in place of your selection | **Replace** (pastes over the text you selected, then puts your clipboard back) |
| Always replace, with no popup | Settings → *Replace the selected text automatically* |
| Close the popup | Esc, the ✕ button, or click elsewhere |
| Settings, pause, usage, updates | Right-click the tray icon |

A single Ctrl+C is always a normal copy. Holding Ctrl+C down never opens the popup.

**Replace** works in any window that accepts Ctrl+V. The app puts the translation on the clipboard, returns
to the window you selected the text in, presses Ctrl+V for you once you have let go of Ctrl, and then puts
your copied text back on the clipboard. Windows does not allow it to type into a program running as
administrator. In that case, and whenever the paste is not possible, the popup appears with the translation
so you can use Copy. With automatic replace on, the popup also appears for errors and for text that is too
long, and "Switched to …" shows as a notification.

## Getting API keys

You need at least one key. Both services have a free monthly allowance, and with two keys the app
switches to the second when the first runs out.

### DeepL API Free

1. Go to <https://www.deepl.com/pro-api> and sign up for the **DeepL API Free** plan.
2. In your DeepL account, open **API Keys** and copy the key. Free keys end in `:fx`.
3. Paste it into Tray Translator and press **Test**.

The Free plan has a monthly character allowance; the tray menu shows your usage and limit as DeepL
reports them. At the limit DeepL refuses further requests (it does not bill a Free plan), and the app
switches to Google until DeepL's allowance resets. A DeepL Pro key also works.

### Google Cloud Translation (500,000 characters a month free)

1. Go to <https://console.cloud.google.com/>, create a project and set up billing for it.
   Google requires billing even for the free allowance.
2. Enable the **Cloud Translation API** for the project:
   <https://console.cloud.google.com/apis/library/translate.googleapis.com>.
3. Open **APIs & Services → Credentials → Create credentials → API key**. Restricting the key to the
   Cloud Translation API is recommended.
4. Paste it into Tray Translator and press **Test**.

> **Google bills beyond its free allowance; it does not stop.** Tray Translator counts every character
> it sends to Google and stops using Google at **480,000 characters** a month (Settings → *Google monthly
> ceiling*). The count resets at midnight US Pacific Time on the 1st, when Google's billing month
> starts. Only if you turn on **Allow paid usage** will the app keep using Google past the ceiling. The
> app only knows about its own use of the key; if the same key is used elsewhere, set a lower ceiling or
> a budget alert in Google Cloud.

## Install

Download `TrayTranslator-win-Setup.exe` from the
[latest release](https://github.com/gotolisto/virgo/releases/latest) and run it. It installs for your
user only (no administrator prompt), adds a Start menu shortcut, and starts the app.

**Windows SmartScreen warning.** The installer is not code-signed yet, so Windows may show *"Windows
protected your PC"*. Click **More info → Run anyway**. Some browsers also ask you to confirm keeping the
download.

The app updates itself from GitHub Releases. It checks at startup and every 24 hours, downloads in the
background, and shows a notification. Click it, or use *Install update and restart* in the tray menu.
Settings and keys are kept across updates.

To uninstall, use **Settings → Apps → Installed apps → Tray Translator → Uninstall**. This also removes
the *Start with Windows* entry.

## Where things are stored

| What | Where |
| --- | --- |
| Settings | `%AppData%\TrayTranslator\settings.json` |
| API keys | `%AppData%\TrayTranslator\keys.dat`, encrypted with Windows DPAPI for your user account |
| Monthly character counts | `%AppData%\TrayTranslator\usage.json` |
| Logs (7 days) | `%LocalAppData%\TrayTranslator\logs` |
| The app itself | `%LocalAppData%\TrayTranslator` |

Logs record events such as which engine answered and HTTP status codes. They never contain the text you
translate, the translation, or a key. Uninstalling removes the app and its logs and keeps `%AppData%`.

## Building

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). On Windows that is
all; Visual Studio 2026 or Rider work too.

```bash
dotnet build
```

```bash
dotnet test
```

Run the app from the build output (`src/TrayTranslator.App/bin/Debug/net10.0-windows/TrayTranslator.exe`).
A development build does not check for updates and does not register itself to start with Windows.

To build the installer locally:

```bash
dotnet publish src/TrayTranslator.App -c Release -r win-x64 --self-contained -o artifacts/publish
```

```bash
dotnet tool restore
```

```bash
dotnet vpk pack --packId TrayTranslator --packVersion 0.1.0 --packDir artifacts/publish --mainExe TrayTranslator.exe --packTitle "Tray Translator" --icon src/TrayTranslator.App/Assets/app.ico --shortcuts StartMenuRoot --runtime win-x64 --outputDir artifacts/releases
```

On Linux or macOS, write `dotnet vpk "[win]" pack …` to cross-package for Windows.

### Project layout

| Project | What it holds |
| --- | --- |
| `src/TrayTranslator.Core` | Everything that is not Windows-specific: the Ctrl+C+C detector, language detection and direction, the DeepL and Google clients, failover, usage tracking, settings and key storage. No WPF or Windows references. |
| `src/TrayTranslator.App` | The WPF app: popup, settings, first-run and About windows, tray icon, and the Windows implementations of Core's platform interfaces (`IHotkeyListener`, `IClipboardReader`, `ITrayIcon`, `IAutostart`, `ISecretStore`, `ICursorLocator`). |
| `tests/TrayTranslator.Tests` | xUnit tests for Core. |

## Releasing

The version lives in one place: `<Version>` in `src/TrayTranslator.App/TrayTranslator.App.csproj`
(MAJOR.MINOR.PATCH). About and the tray tooltip read it from the assembly.

1. Set `<Version>` to the new version, for example `0.2.0`.
2. In `CHANGELOG.md`, move the *Unreleased* entries under a new `## [0.2.0] - YYYY-MM-DD` heading. That
   section becomes the release notes.
3. Commit, then tag and push the tag:

   ```bash
   git tag v0.2.0
   ```

   ```bash
   git push origin v0.2.0
   ```

The **Release** workflow (`.github/workflows/release.yml`) then:
- checks that the tag matches `<Version>`
- runs the tests
- publishes a self-contained win-x64 build
- packages it with Velopack, including a delta from the previous release
- uploads `Setup.exe` and the update packages to a GitHub Release for the tag

Installed copies pick the update up within 24 hours, or straight away with *Check for updates*.

To try the pipeline without publishing anything, run the workflow by hand: **Actions → Release → Run
workflow**. The packages are attached to the run as an artifact.

The repository must be **public**: installed copies download updates from its releases without a
token. The workflow needs no stored secrets; it uses the `GITHUB_TOKEN` that GitHub gives every run.

## Privacy

- The selected text is sent only to the engine that translates it (DeepL or Google), over HTTPS.
- Language detection runs locally (NTextCat), so it sends nothing and costs no characters.
- Nothing is kept: no history of texts or translations, and no telemetry.
- Network use: the translation request, DeepL's usage endpoint (to show usage and notice when the
  allowance resets), and GitHub Releases for updates.
