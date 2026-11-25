<#
.SYNOPSIS
  Builds dist\WireGuardBar-Setup.exe: both executables published self-contained and single-file
  (no .NET runtime needed on the target PC), wrapped by Inno Setup 6 (installer\WireGuardBar.iss).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

foreach ($project in 'src\WireGuardBar', 'src\WireGuardBar.Helper') {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o (Join-Path $dist 'app')
    if ($LASTEXITCODE) { throw "dotnet publish $project failed" }
}

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\WireGuardBar.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }
Write-Host "Built dist\WireGuardBar-Setup.exe ($version)"
