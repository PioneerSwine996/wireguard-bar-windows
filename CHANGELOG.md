## [0.1.0] - 2026-11-25

First version: a Windows port of WireGuard Bar for macOS 1.0.

### Features

- Tray icon (off, locked, split tunnel) with a black flyout: one row per tunnel, Connect/Disconnect pill, connecting
  orb while switching (at least 1.5 s), Manage and Quit.
- Manage window: collapsible tunnel list (rail of numbered badges with status lights), animated route globe, Enabled,
  Route all traffic, connection status, data sent and received, latest handshake, endpoint, On-Demand Wi-Fi networks,
  and Edit Configuration behind Windows Hello or the Windows credential prompt.
- Import `.conf` files; only one tunnel active at a time; split tunnel routes computed from `Address` and `DNS`.
- `wgbar-helper` Windows service behind an access-listed named pipe, driving WireGuard for Windows' tunnel service.
- `install.ps1` / `uninstall.ps1`, and GitLab CI that tests and packages a self-contained zip.

### Design Rationale

- WireGuard for Windows' `/installtunnelservice` replaces `wg-quick`: the tunnel code stays upstream's, and there's
  no patched script to maintain.
- A service with a named pipe replaces sudo: the pipe's access list plays the sudoers rule, and the app verifies the
  pipe's owner so a squatter can't receive configs.
- Configs live under `Program Files` rather than `ProgramData`, because standard users can create folders in
  `ProgramData` ahead of the installer.
- Platform-neutral logic sits in `WireGuardBar.Core` so its tests run on any OS, including the Mac it was written on.

### Notes & Caveats

- Not yet run on Windows; see "Not verified yet" in the README.
- Tunnels started by WireGuard's tunnel service come back after a reboot, unlike `wg-quick` on macOS.
