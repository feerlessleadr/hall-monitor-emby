# Hall Monitor: status

Last updated: 2026-11-03 (docs setup session). This is a living snapshot; overwrite it, do not append.

## Git state (verified 2026-11-03 with `git fetch`)
- Branch `main`: **ahead 4, behind 3** of `origin/main`. Working tree clean.
- Local only (4): 19ced7d "Say why time changed ... (2.5.0.0, not released)", 2551147, fedc9e6, 5274d1b (decision-log updates and `tools\Test-ReasonLive.ps1`).
- Origin only (3): d133ba9 "Apply due resets when the plugin starts", 3072528 "Release 2.4.1.0", e145547 "Record the 2.4.1.0 release". Tag `v2.4.1.0` is on origin but not local.
- `AssemblyVersion` locally is 2.5.0.0.

## Released versions
2.1.0.0 (watch ledger), 2.2.0.0 (`Limits[]`, ApiLevel 4), 2.3.0.0 (reduce can go negative, ApiLevel 5), 2.4.0.0 (limit of 0 = no time left, ApiLevel 6), **2.4.1.0** (due resets applied at start; on origin only).

## In progress: 2.5.0.0 "Say why time changed" (ApiLevel 7), built, NOT released
- Optional `Source` and `Reason` on `Public/ExtendTime` and `Public/ReduceTime`. Admin routes take `Reason`; source is fixed to "plugin".
- New in-memory `TimeChangeLog` keeps the latest 20 changes per user. Status gains `RecentTimeChanges`.
- Source values: app, plugin, homeassistant, other, unknown. Reason is capped at 80 characters, control characters stripped.
- Verified: 33 unit tests; regression suite 74/74 and the 12 live reason checks on the test server (2.5.0.0 was left installed there).
- Not done: the plugin dashboard does not show reasons; the app's notification wording was not checked on an emulator.

## Next actions
1. Ask the owner how to integrate: merge or rebase origin's 2.4.1.0 into local. The fix must survive into 2.5.0.0.
2. After approval and "publish it": write release notes, bump nothing else, tag `v2.5.0.0`.
3. Then the Home Assistant push (`C:\dev\ha`, two local commits waiting) and the Embidextrous release.

## Open questions
- Home Assistant sends a user name as `UserId`; the plugin matches an Emby user id. Do chore additions land for Harper, Penny and Wyatt? Check on the live server once 2.5.0.0 is out.
- Is the production Emby server on Windows? (Owner says it runs as the tray app under a separate Windows account; see WORKSPACE.md.)
- Should the `watching-eye` clone be deleted after the integration? Not before.

## Known issues and gotchas
- `ReduceTime` on a 0 limit creates hidden debt.
- A blank limit field saves as 0 (now blocked by validation).
- Weekly reset must be on for "weekly" to mean a week.
- Ledger history starts at install; a backfill from Playback Reporting was offered but is not built.
- Regression push-count test has a timing flake. The regression suite is not in CI.
- Backlog: hash the stored API key, admin audit trail, restrict the public API by client address.

## Not verified
- 2.5.0.0 on the production server, and on Linux or Docker servers.
- App notification text for reasons on a real phone.
