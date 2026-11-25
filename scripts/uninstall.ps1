#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Removes WireGuard Bar. Tunnels it started are stopped. Configs are kept unless -RemoveConfigs is given.
#>
param([switch]$RemoveConfigs)

$ErrorActionPreference = 'Stop'
if (-not $env:ProgramFiles) { throw 'ProgramFiles is not set; refusing to guess the install folder.' }
$dest = Join-Path $env:ProgramFiles 'WireGuardBar'
$service = 'wgbar-helper'
$wireguard = Join-Path $env:ProgramFiles 'WireGuard\wireguard.exe'
$configs = Join-Path $dest 'Data\Configurations'

Get-Process WireGuardBar -ErrorAction SilentlyContinue | Stop-Process -Force

# Stop tunnels that were started from our configs.
if ((Test-Path $wireguard) -and (Test-Path $configs)) {
    foreach ($conf in Get-ChildItem $configs -Filter *.conf) {
        if (Get-Service "WireGuardTunnel`$$($conf.BaseName)" -ErrorAction SilentlyContinue) {
            & $wireguard /uninstalltunnelservice $conf.BaseName
        }
    }
}

if (Get-Service $service -ErrorAction SilentlyContinue) {
    Stop-Service $service -Force
    sc.exe delete $service | Out-Null
}

Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\WireGuard Bar.lnk') -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'WireGuard Bar' -ErrorAction SilentlyContinue

if (Test-Path (Join-Path $dest 'WireGuardBar.exe')) {   # only ever delete a folder that is really ours
    if ($RemoveConfigs) {
        Remove-Item $dest -Recurse -Force
    } else {
        Get-ChildItem $dest -Exclude Data | Remove-Item -Recurse -Force
        Write-Host "Configs kept in $configs. Run with -RemoveConfigs to delete them."
    }
}
Write-Host 'Uninstalled.'
