<#
.SYNOPSIS
  Builds dist\WireGuardBar-Setup.exe: both executables published self-contained and single-file
  (no .NET runtime needed on the target PC), wrapped by Inno Setup 6 (installer\WireGuardBar.iss).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

# Version: from the tag being built (GitLab or GitHub), else Directory.Build.props.
$tag = if ($env:CI_COMMIT_TAG) { $env:CI_COMMIT_TAG } elseif ($env:GITHUB_REF_TYPE -eq 'tag') { $env:GITHUB_REF_NAME } else { '' }
$version = if ($tag -match '^v(\d+\.\d+\.\d+)$') { $Matches[1] } else {
    ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
}

# Inno Setup: use an installed copy (GitHub's Windows image has one), else install it.
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) {
    choco install innosetup -y --no-progress
    if ($LASTEXITCODE) { throw 'Installing Inno Setup failed' }
}

foreach ($project in 'src\WireGuardBar', 'src\WireGuardBar.Helper') {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none "-p:Version=$version" -o (Join-Path $dist 'app')
    if ($LASTEXITCODE) { throw "dotnet publish $project failed" }
}

& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\WireGuardBar.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }
Write-Host "Built dist\WireGuardBar-Setup.exe ($version)"
