# wsl-import CLI

This is a standalone native CLI for creating and deleting imported WSL distros.

## Commands

- `wsl-import --create`
- `wsl-import --delete <distro-name>`
- `wsl-import --help`

## Build

```powershell
dotnet publish .\wsl-import-cli.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Published executable:

- `bin\Release\net8.0\win-x64\publish\wsl-import.exe`

## End-user installer (single double-click EXE)

Build the self-contained installer with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

This produces a **single file**: `wsl-import-setup.exe`

Distribute only that one file. When the user double-clicks it:

- `wsl-import.exe` is extracted and installed to `%LOCALAPPDATA%\Programs\wsl-import`
- That folder is permanently added to the user PATH
- No admin rights required

Open a new terminal and run `wsl-import --help`.

## One-command build + install (for development)

From the `wsl-import-cli` folder, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\quick-install.ps1
```

This will:

- Build and publish `wsl-import.exe`
- Install it to `%LOCALAPPDATA%\Programs\wsl-import`
- Add that folder to user PATH (if needed)
- Make `wsl-import` available in the current terminal session

## Winget manifest templates

Template files are in the `winget` folder. Update placeholders before publishing.
