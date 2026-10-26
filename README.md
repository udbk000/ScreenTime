# ScreenTime – Local Screen Time Tracking & Usage Analytics for Windows

A locally running Windows desktop application inspired by Apple's **Screen Time**, built with **C# / .NET 8 / WinUI 3 / SQLite**.

## Key Features

### 1. Readable App Names & Application Icons
- Displays clean, user-friendly application names (e.g., `Google Chrome`, `Adobe Acrobat Reader`, `Visual Studio Code`) instead of raw executable file paths.
- Automatically extracts application icons from Windows metadata and stores them in a local icon cache (`%LOCALAPPDATA%\ScreenTime\icons\`).
- Groups related processes belonging to the same application using stable application identifiers.

### 2. Intelligent Idle Detection for Studying & Reading
- Monitors mouse and keyboard activity using `GetLastInputInfo`, as well as screen locking and standby through Windows system events.
- **Category-specific and app-specific idle thresholds:**
  - A dedicated **Learning & Reading** category with a default idle threshold of **15 minutes (900 seconds)** for PDF readers (Adobe Acrobat, SumatraPDF, Foxit, PDF24, Drawboard), note-taking apps (Obsidian, Notion, OneNote), and learning tools (Anki, Zotero, Calibre).
  - Usage time continues to count as active until the configured idle threshold is reached. If no mouse or keyboard input occurs beyond that threshold, tracking pauses until the next user input.
  - All thresholds can be customized per category or individually for each application in the settings.

### 3. Usage History, 30-Day Overview & 6-Week Average
- **Dashboard:** Shows today's active usage time, comparison with the 6-week average, an hourly daily timeline (00:00–23:00), a 7-day usage overview, top applications, and category breakdowns.
- **History:** Switch between *Last 7 Days*, *Last 30 Days*, and the *6-Week Average (42 Days)*, with interactive day selection for detailed analysis.
- **6-Week Average:** Includes all days starting from the first day of tracking. Days before tracking began are excluded.

### 4. Light Mode, Dark Mode & System Theme
- Seamlessly switch between **Follow System**, **Light Mode**, and **Dark Mode** via the header or settings.

## Project Structure

- `src/ScreenTime.Core` — Core platform logic, Win32 interoperability, SQLite persistence (`winsqlite3.dll`), application and icon resolution, idle detection, and statistics calculations.
- `src/ScreenTime.App` — WinUI 3 user interface (MVVM), system tray integration, and charts.
- `tests/ScreenTime.Tests` — Automated tests for application resolution, learning/PDF idle thresholds, the 6-week average, and SQLite recovery after restart.

## Installation

The Windows installer (`.exe`) is available as a downloadable asset under **[GitHub Releases](../../releases)**.

1. Open the [Releases](../../releases) page.
2. Select the desired release.
3. Download the Windows installer `.exe`.
4. Run the installer and follow the on-screen instructions.

> **Note:** Windows may display a security warning if the installer is not signed. Only run the installer if you trust its source.

## Privacy

ScreenTime is designed to keep usage data local. It does not require a cloud account or cloud synchronization.

## License

No license has been specified yet.
