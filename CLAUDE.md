# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Trowser

A WinUI 3 Windows desktop application ("a browser that lives in the tray") — users add multiple browser shortcuts that appear as tray icons; clicking one opens a flyout WebView2 panel or a pop-out window.

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
3. `BrowserCacheService` creates `BrowserPage` instances on demand (no persistent caching — always fresh)
4. Clicking a tray icon shows a `BrowserPage` flyout (400×500px); a pop-out button opens `BrowserWindow`

**Settings:** `SettingsWindow` → `SettingsPage` → `SettingsViewModel` — full CRUD for browser configs. Theme switching via `ThemeSelectorService`. Settings stored at `%LocalAppData%/Trowser/ApplicationData/LocalSettings.json`.

**Logging:** `App.Log()` writes to Debug output and `%LocalAppData%/Trowser/trowser-debug.log`.

## Signing / Packaging

MSIX signing uses Azure Code Signing — config is in `Trowser/metadata.json`:
- Endpoint: EUS, Account: `JoeFinAppsSigningCerts`, Profile: `JoeFinApps`
- `AppxPackageSigningEnabled` is `False` in the csproj; signing is done as a post-build step via `signtool` / Azure CLI.
- See `C:\Users\josep\.claude\projects\D--source-trowser\memory\project_artifact_signing.md` for the working signing command.

## Platform Targets

x64 and ARM64 only (x86 removed). Default platform is x64. Target minimum: Windows 10 Build 19041.
