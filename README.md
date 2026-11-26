# lazy clean

Fast, lightweight Windows disk cleanup and maintenance utility written in pure C# WinForms with zero external dependencies.

> **You pick what goes. Nothing is deleted until you say so.**

## Download

Grab the latest standalone `LazyClean.exe` from [GitHub Releases](https://github.com/wildanzm/lazy-clean/releases) (no installation required).

## Features

- **Quick Scan**: Targets Windows Temp, User Temp, browser caches (Chrome, Edge, Firefox), thumbnail/icon caches, crash dumps, Windows Error Reporting, and system logs.
- **Full Drive Scan**: Searches for junk extensions (`.tmp`, `.temp`, `.part`, `.crdownload`), empty directories, duplicate files (via size + SHA-256 hash verification), and oversized files (> 500 MB).
- **Safe Deletion**: Moves items to the Recycle Bin by default with an explicit toggle for permanent deletion. Double-checks file protection and app ownership at delete time.
- **System Maintenance Tools**:
  - Windows Component Store cleanup (`DISM /AnalyzeComponentStore`, `/StartComponentCleanup`, `/ResetBase`).
  - System file compression (`CompactOS` query / enable / disable).
  - One-off cleanups: Empty Recycle Bin, toggle Hibernation (`powercfg /h off`), remove `C:\Windows.old`.
- **Responsive Filtering & Refresh**: Live search, filter chips (`All`, `Checked`, `Unchecked`), sortable virtual DataGridView, and instant refresh (`Refresh` button or `F5`).

## Safety Guarantees

1. **Application & Profile Protection**: Installed software trees (`\Application\`, `\Current\`, `\app-`), updater backups (`.bak-*`, `.old`), program binaries (`.exe`, `.dll`, `.pak`), and Chromium/Firefox profile databases (`*.log` inside LevelDB) are strictly excluded at scan time and re-verified at delete time.
2. **System & Volume Protection**: Drive roots (`C:\`, `D:`), Windows OS directories (`System32`, `SysWOW64`, `WinSxS`), and volume recovery folders (`$Recycle.Bin`, `System Volume Information`, `Recovery`) are blocked across all drives.
3. **TOCTOU & Symlink Guard**: Path canonicalization defeats traversal (`..`) and long-path prefix bypasses; reparse points (symlinks, junctions) and non-empty directories are rejected prior to deletion.
4. **Privilege Isolation**: System maintenance binaries (`dism.exe`, `compact.exe`, `powercfg.exe`, `cmd.exe`) are explicitly resolved from `%WINDIR%\System32`.

## Project Structure

```
├── .github/
│   └── workflows/
│       ├── ci.yml                  # GitHub Actions build & test workflow
│       └── release.yml             # GitHub Actions release workflow for v* tags
├── assets/
│   ├── app.ico                     # Multi-resolution application icon
│   └── icon.svg                    # Vector icon source
├── src/
│   └── LazyClean/
│       ├── Core/
│       │   ├── Models.cs           # Data models, scan options & formatting utilities
│       │   ├── ScanEngine.cs       # Parallel disk scanner, SHA-256 dedupe & safe delete
│       │   ├── Targets.cs          # Scan rules, path canonicalization & protection guards
│       │   └── ViewLogic.cs        # Filtering & sorting logic
│       ├── Properties/
│       │   ├── app.manifest        # UAC requireAdministrator & high-DPI manifest
│       │   └── AssemblyInfo.cs     # Assembly metadata & test visibility
│       ├── UI/
│       │   ├── MainForm.cs         # WinForms UI, virtual grid, toolbar & system tools
│       │   └── Theme.cs            # WCAG-verified color tokens & typography
│       ├── LazyClean.csproj        # MSBuild project configuration (.NET Framework 4.5)
│       └── Program.cs              # Application entry point
├── tests/
│   └── LazyClean.Tests/
│       ├── LazyClean.Tests.csproj  # Test runner project configuration
│       └── SelfTest.cs             # Regression unit tests & UI smoke harness
├── .editorconfig                   # Formatting and line-ending rules
├── .gitignore                      # Git ignore rules
├── build.bat                       # Standalone CLI build using built-in Windows csc.exe
├── LazyClean.sln                   # Visual Studio solution file
├── LICENSE                         # MIT License
├── README.md
└── test.bat                        # Automated unit and UI smoke test runner
```

## Building & Testing

### Option 1: Zero-Install CLI Build (Windows Built-in Compiler)
No Visual Studio or .NET SDK installation required. Run directly on any Windows PC:

```bat
build.bat
```

This compiles `bin\LazyClean.exe` using `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.

### Option 2: Visual Studio / MSBuild
Open `LazyClean.sln` in Visual Studio or run:

```bat
msbuild LazyClean.sln /p:Configuration=Release
```

### Running Tests
Runs all 58 automated checks (filesystem safety, path traversal protection, app ownership guards, duplicate detection, and UI smoke assertions):

```bat
test.bat
```

## Requirements

- Windows 7 / 8 / 10 / 11
- .NET Framework 4.5 or higher (pre-installed on Windows)
- Administrator privileges (requested via `src\LazyClean\Properties\app.manifest` for system maintenance and protected temp folders)

## License

MIT License. See [LICENSE](LICENSE) for details.
