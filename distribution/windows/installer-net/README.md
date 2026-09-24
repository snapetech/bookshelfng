# BookshelfNG Windows Installer

A self-contained .NET WPF installer for deploying BookshelfNG as a Windows service. It keeps the compatible Readarr service and data paths used by existing installations.

## Why a new installer?

The previous installer handled basic file extraction and service registration, but lacked safe upgrade semantics. A failed upgrade could leave a BookshelfNG installation in a broken state with no easy way back. This installer is built around a single principle: **upgrades should never leave you worse off than before**.

Every upgrade automatically backs up the running installation, and if the new version fails to start, the previous version is restored without user intervention.

## Features

### Safe upgrade pipeline

The installer follows a strict sequence when upgrading an existing installation:

1. **Stop** the running BookshelfNG service (registered under the compatible `Readarr` service name)
2. **Back up** binaries, database, config, and user backups to `C:\ProgramData\Readarr.backups\<timestamp>\`
3. **Record** the upgrade in the Windows Registry (`HKLM\SOFTWARE\Readarr\Upgrade`) so it can be undone later
4. **Extract** the new payload into `bin/` using an atomic rename (`bin/` -> `bin.old/`, `bin.update/` -> `bin/`)
5. **Install** the Windows service via `Readarr.Console.exe /i`
6. **Verify** the service reaches `RUNNING` within 60 seconds
7. **Auto-rollback** if verification fails — the pre-upgrade backup is restored automatically

Fresh installs skip steps 1-3 and go straight to extraction and service registration. The installer requests administrator privileges because installing and controlling a Windows service requires elevation.

### Backup strategy

- **Robocopy** with 8-thread parallelism for fast binary copying
- SQLite `PRAGMA integrity_check` on the backed-up database to catch corruption early
- Automatic rotation — only the 2 most recent backups are kept
- Installer artifacts (`Rollback-To-Pre-Upgrade.bat`, `upgrade-helpers/`) are stripped from backup copies to avoid conflicts on restore

### Restore from backup

The installer detects previous upgrade records from the registry and offers a **Restore previous** button. This runs `rollback.ps1` which:

- Reads `HKLM\SOFTWARE\Readarr\Upgrade` to locate the backup
- Stops the service, restores files (supporting both `FileCopy` and `ApiZip` backup methods), and restarts
- Can also be invoked directly: `BookshelfNG.Installer.exe /restore`

### UI

- WPF with [WPF UI](https://github.com/lepoco/wpfui) (Fluent Design / Mica backdrop)
- Real-time log output with color-coded errors
- Progress bar with per-file extraction tracking
- Auto-scrolling log that respects manual scroll position

## Architecture

```
installer-net/
  App.xaml(.cs)              Application entry, /restore flag handling
  MainWindow.xaml(.cs)       UI shell, IUiReporter implementation
  Resources/
    Readarr.ico              Application icon (exe manifest)
    Readarr.png              In-window branding
  Pipeline/
    InstallPipeline.cs       Orchestrates upgrade and restore flows
    BackupService.cs         File-copy backup with robocopy + SQLite verification
    ServiceLifecycle.cs      Stop / delete / poll Windows service via ServiceController
    PayloadExtractor.cs      Extracts embedded zip, atomic bin/ swap
    RegistryService.cs       Read/write HKLM upgrade records
    RollbackRunner.cs        Extracts and runs embedded rollback.ps1
    PowerShellRunner.cs      PowerShell process wrapper
    ProcessStreamer.cs       Generic child process with streamed output
    IUiReporter.cs           UI abstraction for pipeline -> window communication
```

## Build

The installer is built via `build.sh`:

```bash
./build.sh --backend --frontend --framework net10.0 --runtime win-x64
./build.sh --packages --framework net10.0 --runtime win-x64
./build.sh --installer
```

This:
1. Builds Readarr and packages it (`_artifacts/win-x64/net10.0/Readarr/`)
2. Zips the payload (excluding `Readarr.Update`) into an embedded resource
3. Publishes a single-file self-contained `BookshelfNG.Installer.exe` to `distribution/windows/setup/output/`

For development builds without a full Readarr build:

```bash
cd distribution/windows/installer-net
dotnet publish Readarr.Installer.csproj -c Release -r win-x64 --self-contained -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o ../setup/output/
```

Note: without the payload zip, the installer will build and launch but fail at extraction.

## Runtime paths

| Path | Purpose |
|------|---------|
| `C:\ProgramData\Readarr\` | Installation root (config.xml, readarr.db) |
| `C:\ProgramData\Readarr\bin\` | Application binaries |
| `C:\ProgramData\Readarr.backups\` | Pre-upgrade backup snapshots |
| `HKLM\SOFTWARE\Readarr\Upgrade` | Registry: upgrade metadata, backup location, versions |

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| WPF-UI | 4.3.0 | Fluent Design controls and Mica backdrop |
| Microsoft.Data.Sqlite | 10.0.12 | SQLite integrity check on backed-up database |
| System.ServiceProcess.ServiceController | 10.0.12 | Windows service stop/status polling |
