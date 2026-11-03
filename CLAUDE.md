# Hall Monitor: notes for Claude

**Persistent docs:** read `C:\dev\_docs\WORKSPACE.md` (environment, how projects connect), then `STATUS.md` here. Search `DECISIONS.md` only when a past decision matters. The rules for keeping docs current live in `C:\dev\CLAUDE.md`. This repo is public: never write LAN addresses, internal host names, Windows user names, work email, share names or secret values into it.

## State (2026-11-03)
- Local and origin are in sync. Release **2.5.0.0** (say why time changed, ApiLevel 7) is published: tag `v2.5.0.0` on a7c7c9a, merged with 2.4.1.0. `HallMonitor.dll` 232,960 bytes, SHA-256 `6ef6470b53a0862b1759efaf30b1e165cfe0abab2724abce1f8305100c08e247`.
- The other clone, `C:devwatching-eye`, is deprecated. This repo is the live source.

## What it is
Emby Server plugin that limits watch time for chosen users: daily, weekly, monthly and yearly caps, per-day limits, allowed time windows, extra time (extend and reduce), time-outs, stop-stream, an event log, a key-gated public API, and a live websocket status push for the Embidextrous app. A permanent watch ledger (since 2.1) reports time actually watched. It began as a decompile of the third-party WatchingEye 1.6.8.0 plugin, then was hand-rewritten; see README for the provenance notice. There is deliberately no LICENSE file.

## Stack
C#, `net6.0`, nullable annotations, overflow checks on, `packages.lock.json` (restore with `--locked-mode`). One dependency: NuGet `MediaBrowser.Server.Core` 4.9.1.90. Tests: `net8.0`, xUnit, compile the pure-C# files directly. Tested on Emby Server 4.10.1.0. Plugin GUID `e8c3b1b3-4f56-4f38-a28a-2e6c5a043007` (never change it; Emby identifies the plugin by it).

## Commands (PowerShell)
- Build: `dotnet build src/HallMonitor -c Release` (output `src/HallMonitor/bin/Release/net6.0/HallMonitor.dll`).
- Unit tests: `dotnet test tests/HallMonitor.Tests`.
- Regression suite against a TEST Emby server only: `powershell -ExecutionPolicy Bypass -File tools\Run-RegressionTests.ps1 [-BaseUrl ...] [-SkipEnforcement] [-SkipLockout]`. It needs an admin API key in a file in the user profile (see `TESTING.md`). The lockout check locks the public API for 5 minutes, so use `-SkipLockout` on reruns. `tools\Test-ReasonLive.ps1` runs the 2.5 reason checks live.
- The test server is shared with other sessions. Check for active sessions before restarting it, and restore plugin DLLs afterwards.

## Release and deploy
- Only when the owner says "publish it". Tag `v<AssemblyVersion>` (must equal `src/HallMonitor/Properties/AssemblyInfo.cs`); `.github/workflows/release.yml` runs tests, restores locked, builds, writes the SHA-256, attests provenance and creates the release. Write `docs/releases/v<version>.md` first.
- Production gets it through the owner's GitHub Repo Plugin Installer plugin (`C:\dev\githib-repo-update`), then an Emby restart. Manual fallback: copy `HallMonitor.dll` into the Emby `plugins` folder and restart. Two copies with different DLL names stop Emby loading the plugin.
- Planned order for 2.5.0.0: plugin release, then Home Assistant push, then Embidextrous app release. Each step is backward compatible.

## Contract (this repo owns it)
Routes under `/emby/HallMonitor/`: `Public/Status` (GET) and `Public/{ExtendTime,ReduceTime,ResetUserTime,TimeOutUser,ClearTimeOut,StopStream}` (POST JSON). Two headers: `X-HallMonitor-Api-Key` (plugin key) and `X-Emby-Token`. Websocket: client sends `HallMonitorStatusStart` with `0,30000,<plugin key>`, server pushes `HallMonitorStatus`. Changes must be additive; bump `ApiLevel` for new status fields (7 = `RecentTimeChanges`, `Source`, `Reason`). Clients: Embidextrous app, Home Assistant. Details: `C:\dev\_docs\WORKSPACE.md`, `PROGRESS.md` sections 8, 12, 14, 21.

## Conventions
- Admin routes use `[Authenticated(Roles="Admin")]`. Only listed users are affected. User names are kept exactly.
- Limits: minutes 1..43200 per request, credit cap 10 years, API lockout after 5 wrong keys for 5 minutes (HTTP 403), key at least 24 characters.
- Names are fixed since 2.0: `HallMonitor.dll`, `/emby/HallMonitor/...`, header `X-HallMonitor-Api-Key`, socket `HallMonitorStatus`. Old WatchingEye names are not answered.
- Emby caches a plugin page per version: bump the version for any page change.
- Commits as `feerlessleadr` with the GitHub noreply-style address from the owner's other repos; commit with `git commit -F <file>`. Nothing is pushed or tagged without the owner's explicit approval.
- Never print keys or tokens. State what was NOT verified.

## Gotchas
- Home Assistant sends `user_id: "Harper"` (a name). The plugin matches an Emby user id via `ResolveListedUser`. Open question: confirm chore adds actually land (see `STATUS.md`).
- `EffectiveDailyLimitMinutes` is 0 for both "no limit" and "limit 0": use the `Limits` list.
- `ReduceTime` on a 0 limit creates hidden debt paid by the next extension.
- Regression push-count test has a known timing flake.
- Line endings differ between the two clones (CRLF vs LF), so a plain `diff` of the trees looks different everywhere.

## Docs map
`PROGRESS.md` (dated log, decision table in section 2), `docs/reason-for-time-changes.md` (2.5 decisions), `docs/releases/`, `TESTING.md`, `SECURITY.md`, `STATUS.md`, `DECISIONS.md`.
