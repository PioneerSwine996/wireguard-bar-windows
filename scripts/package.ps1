<#
.SYNOPSIS
  Builds dist\WireGuardBar-windows.zip: install.ps1, uninstall.ps1, README, LICENSE and app\ with
  two self-contained single-file executables (no .NET runtime needed on the target PC).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'WireGuardBar'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

foreach ($project in 'src\WireGuardBar', 'src\WireGuardBar.Helper') {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o (Join-Path $out 'app')
    if ($LASTEXITCODE) { throw "dotnet publish $project failed" }
}

Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1'), (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $out
Compress-Archive -Path $out -DestinationPath (Join-Path $dist 'WireGuardBar-windows.zip')
Write-Host 'Built dist\WireGuardBar-windows.zip'
