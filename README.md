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

## End-user installer

Build the self-contained installer with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

This produces: `wsl-import-setup.exe`

Distribute only that one file. When run:

- `wsl-import.exe` is extracted and installed to `%LOCALAPPDATA%\Programs\wsl-import`
- That folder is permanently added to the user PATH

Open a new terminal and run `wsl-import --help`.

## One-command build + install (for development)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\quick-install.ps1
```

This will:

- Build and publish `wsl-import.exe`
