#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs (or upgrades) WireGuard Bar: the app, and the wgbar-helper service that runs tunnels as SYSTEM.

.DESCRIPTION
  Copies the app to "C:\Program Files\WireGuardBar" and registers the wgbar-helper service. Only the
  account given with -User (default: the account running this script) can talk to the service, the
  Windows counterpart of the macOS sudoers rule. Tunnel configs live in
  "C:\Program Files\WireGuardBar\Data\Configurations", readable only by SYSTEM and Administrators.

.PARAMETER User
  The Windows account that will use the app, e.g. "PC\alex". Needed only when you elevate with a
  different administrator account than the one you use day to day.
#>
param([string]$User)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'app'
$dest = Join-Path $env:ProgramFiles 'WireGuardBar'
$service = 'wgbar-helper'

if (-not (Test-Path (Join-Path $env:ProgramFiles 'WireGuard\wireguard.exe'))) {
    Write-Warning 'WireGuard for Windows is not installed. WireGuard Bar uses its tunnel service: https://www.wireguard.com/install/'
}

$me = [Security.Principal.WindowsIdentity]::GetCurrent().User
$sid = if ($User) { (New-Object Security.Principal.NTAccount $User).Translate([Security.Principal.SecurityIdentifier]) } else { $me }

# Upgrade: stop the running app and service so their files can be replaced.
Get-Process WireGuardBar -ErrorAction SilentlyContinue | Stop-Process -Force
$existing = Get-Service $service -ErrorAction SilentlyContinue
if ($existing) { Stop-Service $service -Force }

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $source '*') $dest -Recurse -Force

$binPath = "`"$dest\wgbar-helper.exe`" --user $($sid.Value)"
if ($existing) {
    sc.exe config $service binPath= $binPath start= auto | Out-Null
} else {
    New-Service -Name $service -BinaryPathName $binPath -DisplayName 'WireGuard Bar helper' `
        -Description 'Starts and stops WireGuard tunnels for WireGuard Bar.' -StartupType Automatic | Out-Null
}
sc.exe failure $service reset= 86400 actions= restart/5000/restart/5000 | Out-Null
Start-Service $service

# Start menu shortcut for everyone; sign-in autostart for the installing account.
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\WireGuard Bar.lnk'))
$link.TargetPath = "$dest\WireGuardBar.exe"
$link.Save()
if ($sid -eq $me) {
    Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'WireGuard Bar' "`"$dest\WireGuardBar.exe`" --background"
} else {
    Write-Host "To start WireGuard Bar at sign-in, add it to $User's startup apps."
}

Write-Host 'Installed. Open WireGuard Bar from the Start menu and import a .conf file with +.'
