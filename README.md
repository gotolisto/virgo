# Tray Translator

A small Windows 10/11 tray app. Select text in any program, hold **Ctrl** and press **C** twice, and the
translation appears in a popup next to the cursor.

- Works in any app that can copy text: browsers, Notepad, Word, Outlook, PDF readers.
- Translates between your two languages (English ⇄ Spanish by default). Text in any other language is
  translated into your first language.
- Uses **DeepL API Free** and **Google Cloud Translation**, with your own keys. When one runs out of
  its monthly allowance, the other takes over.
- **AI actions:** select text, press a shortcut of your own twice (for example **Alt+C+C**), and Claude
  rewrites it in place, following an instruction you write: "polish this", "polish this and return it in
  Spanish", "make it more formal".
- Leaves your clipboard alone unless you press **Copy** in the popup.
- No history, no telemetry. The selected text goes only to the service in use, over HTTPS.

## Using it

| Action | How |
| --- | --- |
| Translate | Select text, hold Ctrl, press C twice quickly (within 0.5 s by default) |
| Translate the other way | The ⇄ button in the popup header |
| Translate into another language | Click the target language in the popup header (e.g. **ES ▾**) and pick one. Add languages under Settings → *More languages*. The next Ctrl+C+C goes back to A ⇄ B. |
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
so you can use Copy.

Before replacing, the app asks Windows (through its accessibility interface, the one screen readers use)
whether the selected text can be edited. Text on a web page, a label or a link cannot: there the popup shows
the translation instead, even with automatic replace on, and the Replace button is hidden. If an app does
not say, the paste is tried as before. With automatic replace on, the popup also appears for errors and for text that is too
long, and "Switched to …" shows as a notification.

## AI actions

An AI action is a shortcut plus an instruction for Claude. Select text in any app, hold the shortcut's
modifier and press its key twice (Alt+C+C: hold **Alt**, press **C** twice). The app copies the selection,
Claude follows the instruction, and the result replaces your selection. A small popup with the action's
name shows while Claude works; press Esc to cancel.

Set them up under Settings → *AI actions (Claude)*:

1. Paste your Claude API key and press **Test** (see [Claude API](#claude-api) below).
2. Pick a model. **Claude Opus 5.5** gives the best results; **Sonnet 5.5** and **Haiku 5.5** answer faster
   and cost less.
3. For each action, write a name and the instruction, choose the modifier (**Hold**) and the key
   (**Press twice**), and tick **Active**. **Add action** adds another, up to 20.

The app starts with two examples, both off until you tick **Active**:

| Action | Shortcut | Instruction |
| --- | --- | --- |
| Polish | Alt+C+C | Fix spelling, grammar and punctuation, keep meaning, tone and length, reply in the text's own language |
| Polish in Spanish | Alt+S+S | Polish the text and return it in Spanish |

Things to know:

- **Fn can't be used.** On almost every keyboard the Fn key is handled inside the keyboard and Windows
  never sees it. Use Alt, Ctrl + Alt, Ctrl + Shift, Alt + Shift or Win + Alt.
- **The keys still reach the app you are in.** Tray Translator only listens; it never swallows keys. Pick
  a combination your apps do not use. In Office, Alt with a letter can open a ribbon tab.
- Ctrl with C is reserved for translation. Two active actions cannot share a shortcut.
- Unlike Ctrl+C+C, the shortcut does not copy anything itself, so the app presses Ctrl+C for you once you
  let go of the keys. Afterwards it puts your clipboard text back. If the clipboard held something other
  than plain text (an image, a file, formatted text), it is put back as plain text, or, for an image or
  file, the selected text stays on the clipboard.
- As with Replace, text that cannot be edited (a web page, a label) shows the result in the popup with
  **Copy** instead, and so does a window that does not accept the paste.
- Nothing is pasted when Claude declines the request or the answer is cut off; the popup says why.
- With some keyboard layouts AltGr counts as Ctrl + Alt, so a Ctrl + Alt action can fire when you type
  the same AltGr character twice. Use plain Alt for those keys if that happens.

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

To make sure Google can never bill you, also set a daily cap in Google Cloud: **Quotas & System Limits**,
filter by *Cloud Translation API*, and set **"v2 and v3 general model characters per day"** to **16,000**
(16,000 × 31 days stays under the free 500,000). When Google reaches that cap, Tray Translator switches to
DeepL until the cap resets at midnight US Pacific time, then returns to Google.

### Claude API

AI actions need a key for the Claude API. Translation does not.

1. Go to <https://platform.claude.com/settings/keys>, sign in or create an account, and add credit under
   **Billing**. The Claude API is pay as you go, separate from a Claude.ai subscription.
2. Create a key and copy it (it starts with `sk-ant-`).
3. Paste it into Settings → *AI actions (Claude)* and press **Test**. Testing lists the models and costs
   nothing.

Each action is one request to Claude, billed to your account by the tokens it uses (about 4 characters a
token). Rewriting a paragraph typically costs around a cent on Opus 5.5 and a fraction of that on Sonnet
and Haiku. Set a spending limit in the Claude Console if you want a hard cap.

## Install

Download `TrayTranslator-win-Setup.exe` from the
[latest release](https://github.com/gotolisto/virgo/releases/latest) and run it. It installs for your
user only (no administrator prompt), adds a Start menu shortcut, and starts the app.

**Windows SmartScreen warning.** The installer is not code-signed yet, so Windows may show *"Windows
protected your PC"*. Click **More info → Run anyway**. Some browsers also ask you to confirm keeping the
download.

Already installed? Run the newer `Setup.exe` anyway: it detects the installed version and asks
"Version 0.2.1 is currently installed. Would you like to update to version 0.2.2?". Click **Update**. There
is no need to uninstall first. The same version offers **Repair**, an older installer warns before a
**Downgrade**. Settings and keys are kept either way (log files are cleared).

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
| `src/TrayTranslator.Core` | Everything that is not Windows-specific: the Ctrl+C+C and AI action shortcut detectors, language detection and direction, the DeepL and Google clients, failover, usage tracking, the Claude client for AI actions (official Anthropic SDK), settings and key storage. No WPF or Windows references. |
| `src/TrayTranslator.App` | The WPF app: popup, settings, first-run and About windows, tray icon, and the Windows implementations of Core's platform interfaces (`IHotkeyListener`, `IClipboardReader`, `ISelectionCopier`, `ITrayIcon`, `IAutostart`, `ISecretStore`, `ICursorLocator`). |
| `tests/TrayTranslator.Tests` | xUnit tests for Core. |

## Versions and releasing

Every release has a version number, `MAJOR.MINOR.PATCH`, and its own section in
[`CHANGELOG.md`](CHANGELOG.md). Users see that file inside the app: tray menu → **What's new** (also
linked from About), and a notification after each update says what the new version brings.

| Change | Version step | Example |
| --- | --- | --- |
| New feature or visible change | MINOR | 0.2.0 → 0.3.0 |
| Only bug fixes | PATCH | 0.2.0 → 0.2.1 |
| Something that breaks existing settings or habits | MAJOR (after 1.0) | 1.4.0 → 2.0.0 |

**Every change** adds a line to `CHANGELOG.md` under `## [Unreleased]`, in the same pull request, written
for users ("Replace button in the popup…").

**To release**, the version lives in one place: `<Version>` in
`src/TrayTranslator.App/TrayTranslator.App.csproj`. About and the tray tooltip read it from the app.

1. Set `<Version>` to the new version, for example `0.3.0`.
2. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [0.3.0] - YYYY-MM-DD` with today's date, and add a
   fresh empty `## [Unreleased]` above it.
3. Commit, merge to `main`, then tag and push the tag:

   ```bash
   git tag v0.3.0
   ```

   ```bash
   git push origin v0.3.0
   ```

Two checks keep versions and notes together:
- The tests fail if `CHANGELOG.md` has no section with entries for the version in `<Version>`.
- The release workflow fails if the tag does not match `<Version>`, or the section is missing, empty or
  still says *Unreleased*.

The **Release** workflow (`.github/workflows/release.yml`) then:
- runs the tests
- publishes a self-contained win-x64 build
- packages it with Velopack, including a delta from the previous release
- uploads `Setup.exe` and the update packages to a GitHub Release, with the changelog section as its notes

Installed copies pick the update up within 24 hours, or straight away with *Check for updates*.

To try the pipeline without publishing anything, run the workflow by hand: **Actions → Release → Run
workflow**. The packages are attached to the run as an artifact.

The repository must be **public**: installed copies download updates from its releases without a
token. The workflow needs no stored secrets; it uses the `GITHUB_TOKEN` that GitHub gives every run.

## Privacy

- The selected text is sent only to the engine that translates it (DeepL or Google), over HTTPS. For an
  AI action, it is sent only to Claude (Anthropic's API), together with the action's instruction.
- Language detection runs locally (NTextCat), so it sends nothing and costs no characters.
- Nothing is kept: no history of texts or translations, and no telemetry.
- Network use: the translation request, the Claude request for an AI action, DeepL's usage endpoint (to show usage and notice when the
  allowance resets), and GitHub Releases for updates.
