<p align="center">
  <img width="128" align="center" src="Trowser/Trowser.png">
</p>
<h1 align="center">
  Trowser
</h1>
<p align="center">
  A browser that lives in the tray.
</p>

<p align="center">
  <img src="Screenshot-JFA.png" width="45%" alt="Trowser popup panel" />
  &nbsp;
  <img src="Screenshot-settings.png" width="45%" alt="Trowser settings window" />
</p>

## What is Trowser?

Trowser is a WinUI 3 Windows desktop application that lets you pin browser shortcuts as tray icons. Click a tray icon to open a real popup WebView2 window near the tray, then pin it to keep it alive and topmost.

- Add multiple browser shortcuts, each with its own tray icon
- Popup window opens instantly near the system tray
- Popup hides on deactivation unless pinned
- Pin mode keeps the popup window alive, topmost, and movable/resizable
- Configurable popup size per browser
- Mobile emulation enabled by default
- Favicon auto-fetched and cached per config

## Requirements

- Windows 10 Build 19041 (20H1) or later
- [.NET SDK 9.0.311](https://dotnet.microsoft.com/download) (pinned via `global.json`)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- x64 or ARM64 architecture

## Build & Run

```bash
# Build (Debug, x64)
dotnet build

# Build (Release)
dotnet build -c Release

# Run directly
dotnet run --project Trowser/Trowser.csproj

# Publish MSIX
dotnet publish -c Release -p:Platform=x64
```

## Architecture

**Pattern:** MVVM + `Microsoft.Extensions.Hosting` DI container

| Project | Purpose |
|---|---|
| `Trowser.Core` | Models (`TrayBrowserConfig`), `FileService` for JSON persistence, shared helpers |
| `Trowser` | WinUI 3 app — Views, ViewModels, Services |

**Key files:**
- `App.xaml.cs` — single-instance enforcement, DI init, tray icon lifecycle
- `TrayBrowserService` — loads configs from `%LocalAppData%/Trowser/ApplicationData/TrayBrowsers.json`
- `FaviconService` — fetches/caches icons to `%LocalAppData%/Trowser/Icons/{configId}.ico`
- `SettingsWindow` → `SettingsPage` → `SettingsViewModel` — full CRUD for browser configs

**Shared WebView2 environment** cached at `%LocalAppData%/Trowser/WebView2Data`, shared across all `BrowserPage` instances.

## Settings & Data

| Item | Path |
|---|---|
| Browser configs | `%LocalAppData%/Trowser/ApplicationData/TrayBrowsers.json` |
| App settings | `%LocalAppData%/Trowser/ApplicationData/LocalSettings.json` |
| Cached favicons | `%LocalAppData%/Trowser/Icons/` |
| Debug log | `%LocalAppData%/Trowser/trowser-debug.log` |
