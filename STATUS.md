# Hall Monitor: status

Last updated: 2026-11-03 (verified with `git fetch`). Living snapshot; overwrite it, do not append.

## Git state
- Branch `main` is level with `origin/main`. Nothing to push or rebase. Working tree clean.
- Origin's 2.4.1.0 history was merged into the 2.5.0.0 work (merge commit a7c7c9a, not a rebase), so the "due resets applied at start" fix is in 2.5.0.0.
- Tag `v2.5.0.0` is on origin. `AssemblyVersion` is 2.5.0.0.

## Released versions
2.1.0.0 (watch ledger), 2.2.0.0 (`Limits[]`, ApiLevel 4), 2.3.0.0 (reduce can go negative, ApiLevel 5), 2.4.0.0 (limit of 0 = no time left, ApiLevel 6), 2.4.1.0 (due resets applied at start), **2.5.0.0** (published 2026-11-03).

## 2.5.0.0 "Say why time changed" (ApiLevel 7), released
- Optional `Source` and `Reason` on `Public/ExtendTime` and `Public/ReduceTime`. Admin routes take `Reason`; source is fixed to "plugin".
- In-memory `TimeChangeLog` keeps the latest 20 changes per user. Status gains `RecentTimeChanges` (empty after a restart).
- Source values: app, plugin, homeassistant, other, unknown. Reason is capped at 80 characters, control characters stripped.
- Verified before release: 33 unit tests; regression 74/74 and 12 live reason checks on the test server; the app showed all four reason texts on an emulator against this plugin. Those live runs were on the pre-merge tree; after the merge only the build and the 33 unit tests were run. Detail: `PROGRESS.md` section 21 and `docs/reason-for-time-changes.md`.
- Release asset: `HallMonitor.dll` 232,960 bytes, SHA-256 `6ef6470b53a0862b1759efaf30b1e165cfe0abab2724abce1f8305100c08e247`.
- Consumers: Home Assistant already sends Source/Reason (its commits are on origin). Embidextrous 0.11.0 shows the reasons.

## Not done / known issues
- The plugin's own dashboard does not show reasons.
- `RecentTimeChanges` is lost on restart by design.
- Open: Home Assistant sends a user NAME (for example "Harper") as `UserId`. Confirm the plugin resolves names as well as Emby user ids.
- Not recorded: whether 2.5.0.0 is installed on the production server (the installer plugin pulls it on its schedule or on Update).
- Backlog (from `PROGRESS.md`): hash the stored API key, admin audit trail, restrict the Public API by client address, put the regression suite in CI.

## Next actions
1. Owner: update the plugin through the installer and restart Emby; pull the HA config and restart HA; update the app (Obtainium).
2. Confirm 2.5.0.0 is installed on production and that a Home Assistant chore award shows its reason in the phone notification.
3. Optionally re-run `tools\Run-RegressionTests.ps1 -SkipLockout` and `tools\Test-ReasonLive.ps1` against the test server on the merged build (the test server has 2.5.0.0 installed and is closed).
4. Decide whether to show reasons on the plugin dashboard. Nothing is in progress right now.

## Not verified
- 2.5.0.0 on the production server, and on Linux or Docker servers.
- Reason notification text on a real phone and its lock screen.
- The merged 2.5.0.0 build against the live test server.

## In progress
- Nothing. Last session ended with 2.5.0.0, app 0.11.0 and the HA change all published.

## Manual steps for the owner
1. Update Hall Monitor through the GitHub Repo Plugin Installer (daily task or Update button), then restart Emby.
2. Pull the HA config via git-ha-ppens and restart HA.
3. Update the app with Obtainium, then mark one chore done and check the phone notification names it.
