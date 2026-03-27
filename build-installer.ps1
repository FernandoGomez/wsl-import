$ErrorActionPreference = "Stop"

try {
    Set-Location $PSScriptRoot

    Write-Host "Step 1/2: Building wsl-import.exe..." -ForegroundColor Cyan
    dotnet publish .\wsl-import-cli.csproj -c Release -r win-x64 --self-contained true `
        /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

    Write-Host ""
    Write-Host "Step 2/2: Building wsl-import-setup.exe (embeds wsl-import.exe)..." -ForegroundColor Cyan
    dotnet publish .\installer\wsl-import-installer.csproj -c Release

    $setupExe = ".\installer\bin\Release\net8.0\win-x64\publish\wsl-import-setup.exe"
    if (!(Test-Path $setupExe)) {
        throw "Build succeeded but setup exe not found at: $setupExe"
    }

    Copy-Item $setupExe .\wsl-import-setup.exe -Force

    Write-Host ""
    Write-Host "Done! Single-file installer ready:" -ForegroundColor Green
    Write-Host "  $PSScriptRoot\wsl-import-setup.exe" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Distribute only this one file. Double-clicking it installs wsl-import." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "ERROR: $_" -ForegroundColor Red
    Write-Host ""
}
finally {
    Write-Host ""
    Write-Host "Press any key to close..." -ForegroundColor DarkGray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
