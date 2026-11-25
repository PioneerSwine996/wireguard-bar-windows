# WireGuard Bar for Windows

A small tray app for switching WireGuard tunnels on and off, ported from
[WireGuard Bar for macOS](https://gitlab.com/PioneerSwine996/wireguard). It drives the tunnel service of the official
[WireGuard for Windows](https://www.wireguard.com/install/) client, so the tunnels themselves run on WireGuard's own,
well-tested code.

- **Tray icon:** a shield with a slash when nothing is connected, a locked shield when a tunnel routes all traffic,
  an open lock when it's a split tunnel. Clicking it opens a black panel with every tunnel and a Connect/Disconnect
  button. Only one tunnel is active at a time.
- **Manage window:** tunnel list on the left (collapsible to a rail of numbered badges with status lights). On the
  right, a globe shows the route from this PC through your local network to the server (and, for a split tunnel,
  straight out to the internet), then Enabled, Route all traffic, connection status, data sent and received, latest
  handshake and endpoint.
- **Import:** the + button copies a `.conf` file into the helper's config folder, named after the file. It never
  overwrites an existing tunnel.
- **Route all traffic:** on sends everything through the VPN (`AllowedIPs = 0.0.0.0/0, ::/0`); off sends only the
  tunnel's own subnets (from `Address`, host addresses widened to /24 or /64) and its DNS servers. The helper applies
  this to a separate copy; your config file is never changed. Tunnels you never toggle run their config as written.
- **On-Demand:** a list of Wi-Fi networks per tunnel. Joining one connects the tunnel; leaving it disconnects it again
  if On-Demand turned it on.
- **Edit Configuration:** asks for Windows Hello (or your Windows password when Hello isn't set up), then opens the
  config in an editor inside the window.

## Install

1. Download `WireGuardBar-Setup.exe` from the latest release:
   <https://github.com/PioneerSwine996/wireguard-bar-windows/releases/latest/download/WireGuardBar-Setup.exe>
   (also on [GitLab's releases page](https://gitlab.com/PioneerSwine996/wireguard-bar-windows/-/releases)).
2. Run it. One administrator prompt installs the app and its helper service, a Start menu entry and sign-in
   autostart. If [WireGuard for Windows](https://www.wireguard.com/install/) isn't installed, setup offers to
   download and run its official installer.
3. Import a tunnel with +. Windows 11 hides new tray icons in the overflow menu; drag the shield onto the taskbar to
   keep it visible.

If WireGuard for Windows or the helper goes missing, or an update needs a newer helper, the Manage window shows a
setup screen with **Get WireGuard** and **Install** (one administrator prompt) until everything is in place again.

The installer isn't code-signed, so Windows SmartScreen will warn about it the first time: choose **More info** and
**Run anyway**.

Uninstall from **Settings > Apps**. It stops tunnels started from WireGuard Bar's configs and asks before deleting the
configs.

## Build

```powershell
dotnet build WireGuardBar.sln
dotnet test tests/WireGuardBar.Core.Tests
.\scripts\package.ps1          # dist\WireGuardBar-Setup.exe
```

Needs the .NET 10 SDK, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer. The core library and its tests also build and run on macOS and Linux; the app and helper
compile there too (`EnableWindowsTargeting`), but only run on Windows.

## Why C#, .NET 10 and WPF

- **WireGuard for Windows does the tunnelling.** Its `wireguard.exe /installtunnelservice` and `wg.exe` are a stable,
  documented interface, so there's no reason to reach for a lower-level language.
- **WPF over WinUI 3:** WPF ships with .NET, needs no Windows App SDK runtime, and builds with nothing but the .NET SDK,
  which keeps CI simple. Its immediate-mode `OnRender` drawing is a close match for SwiftUI's `Canvas`, which the
  globe and the connecting orb are written against. Borderless, rounded, dark windows are straightforward.
- **The tray icon** uses WinForms' `NotifyIcon`, the standard Win32 tray icon wrapper, rather than a third-party package.
- **.NET 10** is the current long-term-support release (.NET 8's support ended in November 2026). Both executables
  are published self-contained, so the PC doesn't need a .NET runtime.

## How it works

The app never runs elevated. It sends one request per connection to the **wgbar-helper** Windows service over the
named pipe `\\.\pipe\wgbar-helper`: `version`, `status`, `up NAME [ALLOWEDIPS]`, `down NAME`, `addresses NAME`, `read NAME`,
`write NAME` or `import NAME`, the same commands as the macOS helper. The service runs as LocalSystem and:

- accepts connections only from SYSTEM and the one account it was installed for (the counterpart of the macOS sudoers
  rule): the account signed in to the Windows session, even if the administrator prompt was answered with a separate
  admin account. Network logons are refused;
- validates tunnel names (1-32 of `A-Z a-z 0-9 _ = + . -`, no reserved device names) and AllowedIPs overrides (only
  address characters, at most 512);
- keeps configs in `C:\Program Files\WireGuardBar\Data\Configurations`, with an access list that allows only SYSTEM and
  Administrators, and refuses to use that folder if it's a link;
- starts tunnels with `wireguard.exe /installtunnelservice <config>` and stops them with `/uninstalltunnelservice`,
  running WireGuard's tools by full path from `C:\Program Files\WireGuard`, never through a shell;
- strips private and preshared keys from `wg show all dump` before returning status;
- refuses configs with `PreUp`, `PostUp`, `PreDown` or `PostDown` from the app, since WireGuard would run those as
  SYSTEM if its `DangerousScriptExecution` setting were on.

`wgbar-helper.exe install` and `uninstall` (run elevated by the installer, its uninstaller and the setup screen)
register and remove the service. The app only offers to register it from `Program Files`, so the service never runs
an executable a standard user could replace.

Before sending anything, the app checks that the pipe is owned by SYSTEM or Administrators, so another program that
grabbed the pipe name first never receives a config.

WireGuard's tunnel services start automatically with Windows, so a tunnel that was on when you shut down comes back
on at the next boot. Tunnels running from the official WireGuard app show up as a warning in the tray panel.

## Security notes

- **Windows Hello is an in-app check.** Any program running as your account can also send the helper `read` and
  `write`, which means it can read private keys or change a config (though not add script hooks). This is the cost of
  switching tunnels without a UAC prompt each time, the same trade-off as the macOS version.
- Editing the config of a tunnel that is running with Route all traffic off changes the original, not the running
  copy; the change applies the next time the tunnel is enabled (or after Disconnect/Connect), not at the next reboot.

## Not verified yet

This port was written on a Mac. What was checked there: the whole solution compiles (with the .NET 9 SDK and the
Windows targeting packs), and the core tests pass, including the connecting orb against the upstream golden vectors.
Not yet run on Windows:

- the app itself: tray icon drawing, flyout placement next to the tray, Manage window layout, the globe and orb
  animation, dark title bar;
- the helper service end to end (pipe access list, owner check, config folder permissions, `wireguard.exe` calls);
- Windows Hello and the credential-prompt fallback (including Microsoft-account passwords);
- reading the Wi-Fi name without location access on Windows 11 24H2 and later;
- the setup screen's UAC flow, `wgbar-helper.exe install` / `uninstall` (session-user lookup, `sc.exe` calls);
- the Inno Setup script (WireGuard download, upgrade over a running install, uninstall), `package.ps1` and the GitLab
  CI jobs, including whether Chocolatey is present on GitLab's Windows runners;
- whether a client opening the pipe for read/write is granted access with the user's `ReadWrite` rule.

Only x64 builds are produced.

## License

MIT. The connecting orb is a port of [thinking-orbs](https://github.com/Jakubantalik/thinking-orbs) by Jakub Antalik
(MIT); its notice is in `src/WireGuardBar.Core/OrbWeb.cs`. WireGuard is a registered trademark of Jason A. Donenfeld.
