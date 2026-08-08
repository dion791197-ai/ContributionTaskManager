# GitHub Goal

A small always-on-top Windows 11 desktop widget that answers one question at a glance:

> How many GitHub contributions do I still need to make today?

Native WinUI 3 / .NET 8, no Electron, no web view. Real GitHub data via the GraphQL API,
with the access token stored in Windows Credential Manager.

```
┌────────────────────────────────┐
│  ◉ octocat            ↻  ⚙  ✕  │
│                                │
│  7 / 10                   70%  │
│  Contributions today           │
│  ████████████████░░░░░░░░      │
│  3 contributions to goal       │
│  Updated just now              │
└────────────────────────────────┘
```

## Requirements

- Windows 11 (Windows 10 1809+ works; rounded corners and Mica degrade gracefully)
- .NET 8 SDK

There is **no Visual Studio dependency** — the project builds with the plain `dotnet`
CLI. See [Building without Visual Studio](#building-without-visual-studio) for how.

## Getting started

### 1. Build and run

```powershell
dotnet build src\GitHubGoal\GitHubGoal.csproj -c Release
.\src\GitHubGoal\bin\Release\net8.0-windows10.0.19041.0\win-x64\GitHubGoal.exe
```

### 2. Create a GitHub OAuth App

The widget signs in with GitHub's **device flow**, which needs a client ID but *no
client secret* — the right choice for a desktop app, since a secret shipped inside a
binary is not a secret.

1. Go to **github.com → Settings → Developer settings → OAuth Apps → New OAuth App**
2. Fill in any name and homepage URL (they are not used by the device flow)
3. After creating it, tick **Enable Device Flow** and save
4. Copy the **Client ID**

### 3. Connect

Open the widget's **Settings** (⚙), paste the Client ID, then click
**Sign in with GitHub** on the widget. It shows an eight-character code — enter it at
`github.com/login/device`. The resulting token goes straight into Windows Credential
Manager.

## How it works

### Today's contributions

Fetched from the GraphQL API:

```graphql
viewer { contributionsCollection(from: $from, to: $to) { contributionCalendar { ... } } }
```

This is the only place GitHub exposes the contribution count — there is no REST
equivalent. (The REST events endpoint is not a substitute: it omits private
contributions and is heavily cached.)

### "Today" means your local today

`$from` and `$to` are local-midnight boundaries carrying your machine's UTC offset, so
the returned calendar aligns with the Windows clock rather than UTC. At 01:00 in UTC+3
the UTC date is still yesterday; using it would show the wrong number for three hours
every night.

`Utilities/LocalDay.cs` handles the awkward cases — half-hour offsets, the 23-hour
spring-forward day, and the 25-hour fall-back day — and the time zone is re-read on
every refresh so changing it in Windows takes effect immediately.

### Security

| What | Where |
|------|-------|
| GitHub access token | Windows Credential Manager (`GitHubGoal:AccessToken`) |
| OAuth Client ID | `%LOCALAPPDATA%\GitHubGoal\settings.json` — public by design |
| Everything else | Same settings file; contains no secrets |

The token is never logged, never displayed, and never written to disk in plain text.
`CredentialService` uses the advapi32 credential API rather than WinRT's
`PasswordVault`, because `PasswordVault` requires package identity and throws in an
unpackaged app.

## Project layout

```
src/
  GitHubGoal.Core/          Domain + services, no UI dependency (unit tested)
    Models/                 GoalProgress, ContributionData, AppSettings, GitHubUser
    Services/               GitHubService, GitHubAuthService, CredentialService,
                            ContributionService, SettingsService, StartupService
    Utilities/              LocalDay, RelativeTime
  GitHubGoal/               WinUI 3 app
    Views/                  MainWindow (the widget), SettingsWindow
    ViewModels/             MainViewModel, SettingsViewModel
    Interop/                TrayIcon (Shell_NotifyIcon), NativeWindow (DWM, DPI)
    Themes/Glass.xaml       Theme-aware glass brushes and control styles
tests/
  GitHubGoal.Core.Tests/    77 tests: goal maths, timezone boundaries, DST,
                            contribution parsing, error mapping, settings persistence
tools/
  GeneratePri.targets       resources.pri generation without Visual Studio
  priconfig.xml
  make-icon.ps1             Generates Assets/AppIcon.ico
  capture-window.ps1        Screenshots a window for design review
```

Run the tests with:

```powershell
dotnet test tests\GitHubGoal.Core.Tests\GitHubGoal.Core.Tests.csproj
```

## Building without Visual Studio

Two things bite when building an unpackaged WinUI 3 app with only the .NET SDK, both
handled by this repo:

1. **`EnableMsixTooling` must stay unset.** Setting it to `false` skips the import that
   turns `EnableCoreMrtTooling` off, so PRI generation runs and demands
   `Microsoft.Build.Packaging.Pri.Tasks.dll` — a Visual Studio-only assembly. The
   project sets `EnableCoreMrtTooling=false` explicitly instead.

2. **`resources.pri` must still be generated.** Without it the app dies at startup with
   a stowed COM exception (`0xc000027b`) the moment `XamlControlsResources` is merged,
   and the failure is a fail-fast that no managed handler can catch.
   `tools/GeneratePri.targets` drives `makepri.exe` from the
   `Microsoft.Windows.SDK.BuildTools` package to produce it.

## Behaviour notes

- **Always on top** is on by default and toggleable in Settings.
- **Closing** the widget hides it to the notification area; **Quit** in the tray menu
  exits.
- **Launch at startup** writes the per-user `Run` key (the native mechanism for
  unpackaged apps) with a `--startup` flag so it begins hidden in the tray.
- **Offline**: the last successful count stays on screen with an amber dot and
  "Last updated N min ago" rather than blanking out.
- **Window position** is remembered and clamped back onto a visible monitor if the
  display layout changed.
- **Reduce motion** is honoured from both the app setting and the Windows
  "Show animations" system setting.
