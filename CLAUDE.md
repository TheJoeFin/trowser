# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Trowser

A WinUI 3 Windows desktop application ("a browser that lives in the tray") — users add multiple browser shortcuts that appear as tray icons; clicking one opens a popup WebView2 window near the tray with an optional pinned/topmost mode.

## Build & Run

Requires .NET SDK 9.0.311 (pinned in `global.json`) and Windows App SDK.

```bash
# Build (Debug, x64)
dotnet build

# Build (Release)
dotnet build -c Release

# Run directly (Debug)
dotnet run --project Trowser/Trowser.csproj

# Publish MSIX
dotnet publish -c Release -p:Platform=x64
```

No test projects exist in this codebase.

## Architecture

**Pattern:** MVVM + Microsoft.Extensions.Hosting DI container.

**Two projects:**
- `Trowser.Core` — models (`TrayBrowserConfig`), `FileService` for JSON persistence, shared helpers
- `Trowser` — WinUI 3 app with Views, ViewModels, Services

**Key entry point:** `App.xaml.cs::OnLaunched()` — enforces single instance via mutex, initializes DI, loads tray configs, creates tray icons, subscribes to `ConfigsChanged`.

**Shared WebView2 environment:** `App.GetSharedWebViewEnvironmentAsync()` returns a single cached `CoreWebView2Environment` used by all `BrowserPage` instances. User data: `%LocalAppData%/Trowser/WebView2Data`. Mobile emulation is enabled by default.

**Tray icon lifecycle:**
1. `TrayBrowserService` loads `TrayBrowserConfig[]` from `%LocalAppData%/Trowser/ApplicationData/TrayBrowsers.json`
2. `FaviconService` fetches/caches icons to `%LocalAppData%/Trowser/Icons/{configId}.ico`
3. `BrowserCacheService` keeps one `BrowserPage` per config so hiding and reopening preserves WebView2 state
4. Clicking a tray icon toggles a `BrowserWindow` popup near the cursor/tray; unpinned windows hide on deactivation and pinned windows stay topmost, movable, and resizable

**Settings update flow:** Any `TrayBrowserService.SaveAsync()`/`DeleteAsync()` fires `ConfigsChanged` → `App.RefreshTrayIconsAsync()` hides all existing tray icons, clears the dictionary, and recreates them. This is the only way tray icons update at runtime.

**Popup flow:** `BrowserWindow` is now the primary tray surface. It is borderless, hidden from switchers, positioned near the cursor, and uses delayed deactivation checks so WebView2 focus and native context menus do not immediately dismiss it.

**WebView2 non-obvious behaviors:**
- New-window requests (`target="_blank"`) are intercepted and navigated in-place (no system browser spawned).
- Mobile emulation is applied via `Emulation.setDeviceMetricsOverride` DevTools protocol after `CoreWebView2Initialized`.
- `CoreWebView2Environment` is lazy-initialized once (task-cached) and shared across all WebView2 controls.

**Service resolution in code-behind:** XAML code-behind can't receive constructor injection, so ViewModels are resolved via `App.GetService<T>()` (static service locator). New Views should follow this same pattern.

**Settings:** `SettingsWindow` → `SettingsPage` → `SettingsViewModel` — full CRUD for browser configs. Theme switching via `ThemeSelectorService`. Settings stored at `%LocalAppData%/Trowser/ApplicationData/LocalSettings.json`.

**Logging:** `App.Log()` writes to Debug output and `%LocalAppData%/Trowser/trowser-debug.log`.

## Key Dependencies

| Package | Version | Role |
|---------|---------|------|
| `Microsoft.WindowsAppSDK` | 1.8 | WinUI 3 framework |
| `WinUIEx` | 2.9 | `TrayIcon`, `WindowEx`, extended window utilities |
| `CommunityToolkit.Mvvm` | 8.4 | `ObservableObject`, `RelayCommand`, source-gen |
| `Microsoft.Extensions.Hosting` | 10.0 | DI container, `IOptions<T>`, `IConfiguration` |

## Signing / Packaging

MSIX signing uses Azure Code Signing — config is in `Trowser/metadata.json`:
- Endpoint: EUS, Account: `JoeFinAppsSigningCerts`, Profile: `JoeFinApps`
- `AppxPackageSigningEnabled` is `False` in the csproj; signing is done as a post-build step via `signtool` / Azure CLI.
- See `C:\Users\josep\.claude\projects\D--source-trowser\memory\project_artifact_signing.md` for the working signing command.

## Platform Targets

x64 and ARM64 only (x86 removed). Default platform is x64. Target minimum: Windows 10 Build 19041.
