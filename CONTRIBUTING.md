# Contributing

The main repository is on GitLab: <https://gitlab.com/PioneerSwine996/wireguard-bar-windows>.
GitHub (<https://github.com/PioneerSwine996/wireguard-bar-windows>) is a read-only mirror that accepts pull requests.

## Opening a pull request

1. Fork the GitHub mirror and branch from `main`.
2. Keep changes focused; one feature or fix per pull request.
3. Build and test before pushing:

   ```powershell
   dotnet build WireGuardBar.sln
   dotnet test tests/WireGuardBar.Core.Tests
   ```

4. If you change `src/WireGuardBar.Helper` or anything it runs, say so in the pull request and explain how inputs are
   validated. The helper runs as SYSTEM and answers without a UAC prompt, so it gets the closest review.
5. Open the pull request against `main`. GitHub Actions builds and tests it on Windows. Merge requests on GitLab are
   welcome too; GitLab CI builds and tests them.

## How pull requests are merged (maintainers)

GitLab pushes `main` to GitHub, so don't merge with GitHub's button: the next mirror push would overwrite it.
Instead, land the pull request through GitLab:

```bash
git fetch github pull/<number>/head:pr-<number>
git checkout main
git merge --ff-only pr-<number>     # or rebase it onto main first
git push origin main
```

When the mirror pushes `main`, GitHub sees the pull request's commits on `main` and marks it merged.

## Releasing (maintainers)

Tag once and push the tag:

```bash
git tag v1.2.3
git push origin v1.2.3      # origin pushes to both GitLab and GitHub
```

Each site then builds, tests and releases on its own, with nothing shared between them and no secrets to set up:
GitLab CI creates a GitLab Release linking `WireGuardBar-Setup.exe`, and GitHub Actions
(`.github/workflows/release.yml`) creates a GitHub Release with it attached. The app and installer take their version
from the tag. The file name stays fixed, so
<https://github.com/PioneerSwine996/wireguard-bar-windows/releases/latest/download/WireGuardBar-Setup.exe> always points
at the newest installer.

## Style

- C#: follow the existing code; small files, no new dependencies. Logic that doesn't need Windows goes in
  `WireGuardBar.Core` with a test.
- Commits: `type: short description` (`feat`, `fix`, `perf`, `style`, `docs`, `ci`, `refactor`).
