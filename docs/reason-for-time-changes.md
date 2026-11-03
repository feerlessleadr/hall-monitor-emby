# Why time changed: notification reasons (decision log)

Running record of the "say WHY a child's time changed" feature across three repos. Newest entries at the bottom of each section. Add to this file whenever something is decided, however small.

Repos involved
- `hall-monitor-emby` (this repo, the live plugin; `watching-eye` is deprecated and must be ignored)
- `embidextrous` (Android app; notifications built in `notifications/HallMonitorEngine.kt`)
- `feerlessleadr/ha` (Home Assistant config, cloned at `C:\dev\ha`; deployed by git-ha-ppens)

## Request (2026-11-03)
When time is added to a child, the Embidextrous notification should say why. Two sources today:
1. Manual: from the Embidextrous app or the Emby plugin page.
2. Home Assistant: a chore `input_boolean` is turned on and HA calls `POST /emby/HallMonitor/Public/ExtendTime`.
Reductions (app "-30 min", Undo, HA "Trade Tablet Time for Play Time") should be covered too.

## Findings from review (2026-11-03)
- The app does not receive an event. `HallMonitorEngine.kt` (~line 110) compares `creditSecondsDaily` between polls and fires "was given more time" when it rises by 60 s or more. No reason exists in the data.
- `PublicExtendTimeRequest` and `PublicReduceTimeRequest` carry only `UserId` and `Minutes`.
- HA: `rest_command.hall_monitor_extend_time` and `hall_monitor_reduce_time` in `configuration.yaml` post `{"UserId","Minutes"}` with the API key and Emby token as headers.
- HA chore automations that matter are in `automations.yaml` (about lines 4329 Harper, 4622 Penny, 4915 Wyatt); one `choose` branch per chore, fixed minutes. Chore display names are the `input_boolean` names in `packages/chores_tablet.yaml`. The reduction caller is `scripts.yaml` (~line 702, "Trade Tablet Time for Play Time").
- `automations_chores_tablet.yaml` is not included by `configuration.yaml` and looks like a stale copy. Left untouched.
- `config/secrets.yaml` (209 bytes) is tracked in the ha repo despite `.gitignore`. Contents not read. Owner to confirm whether real; rotate keys if so.
- Release pipeline: pushing tag `vX.Y.Z.W` (must equal AssemblyVersion) runs `.github/workflows/release.yml`, which tests, builds, attests and publishes `HallMonitor.dll` plus a `.sha256`. The owner's GitHub Repo Plugin Installer (`feerlessleadr/Emby.GitHubRepoPluginInstall`) installs it on the production Emby server (daily task or Update button, then an Emby restart).

## Decisions
1. Chore label: use a short label per chore that I add in the HA automation (owner OK'd, 2026-11-03). Full helper names such as the morning-chores one are too long.
2. Manual add wording: just say time was manually added by a parent. No app-versus-plugin-page distinction and no parent name in the text. (A `Source` field may still be sent for the record; the text does not depend on it.)
3. Reductions are included: notify when time is taken away, with a reason (owner, 2026-11-03).
4. `hall-monitor-emby` is the live plugin source; `watching-eye` is deprecated (owner).
5. Deployment of the plugin: via GitHub release and the GitHub Repo Plugin Installer, not by copying files.
6. HA deployment: I edit and commit in `C:\dev\ha` only after the owner approves the diff, then push when approved. I confirm when it is live on GitHub; the owner pulls via git-ha-ppens and restarts HA. No HA reload steps needed from me.
7. All changes need the owner's explicit approval before anything is pushed or tagged.
8. Backward compatible: `Source` and `Reason` are optional. Old HA calls, old apps and old plugins keep working; the app falls back to today's wording when no reason exists.

9. Plugin version 2.5.0.0, ApiLevel 7 (owner, 2026-11-03).
10. Trade script reduction reason: "Traded for play time" (owner OK'd).
11. `config/secrets.yaml` in the ha repo checked with values masked: every entry is an empty string, so it is a harmless placeholder. No rotation needed. Left tracked.
12. `automations_chores_tablet.yaml` deleted at the owner's request (nothing referenced it). Committed locally in `C:\dev\ha` as "Remove unused automations_chores_tablet.yaml"; NOT pushed yet. It will go out with the HA change, after the owner approves the combined diff.

## Plan
1. Plugin (`hall-monitor-emby`): optional `Source` and `Reason` on Public/ExtendTime and Public/ReduceTime (and the admin routes). Keep a bounded event list per user (time, minutes, signed, source, reason). Expose recent events in Public/Status and the websocket push. Bump version to 2.5.0.0 and `ApiLevel` to 7. Add unit tests. Release notes in `docs/releases/`.
2. App (`embidextrous`): match each credit change to its event; build text such as "45 min added: Feed Chloe" or "30 min added by a parent"; same for reductions; fall back to the current wording. Update tests, `docs/decisions.md`, `docs/push-contract.md`, `docs/status.md`.
3. HA (`ha`): add `source` and `reason` to the two rest_commands; pass a short chore label from each `choose` branch in `automations.yaml`; pass a reason from the trade script.
4. Order: plugin first (tag after approval), then HA push, then app release. Each is backward compatible so the order is not critical.

## Open items
- Short chore label wording (I will propose a table for approval before editing HA).
- (Resolved: trade wording, secrets.yaml, stale automations file, version; see decisions 9-12.)

## Progress
- 2026-11-03: plugin 2.5.0.0 built locally in `C:\dev\hall-monitor-emby` (not committed, not tagged). 33 unit tests pass, Release build clean. Details in `PROGRESS.md` section 20 and `docs/releases/v2.5.0.0.md`.
- Source values: `app`, `plugin`, `homeassistant`; anything else `other`; missing `unknown`. Reason capped at 80 characters, control characters removed.
- The change list is in memory only (decision: the app only needs recent changes to explain a credit rise; a restart losing it just means the app uses its old wording).

- 2026-11-03: plugin 2.5.0.0 committed locally (not pushed, not tagged). App change written in `C:\dev\embidextrous` (not committed): `TimeChange` model, engine builds the text, manual add/Undo send `Source: "app"`. App unit tests pass (6 new). Wording as decided: manual = "added manually by a parent"; Home Assistant add = "15 min added because the Feed Chloe chore was marked completed."; take-away = "30 min taken away: Traded for play time." or "...manually by a parent."
- Decision: reductions share the existing rule (id `HallMonitor.ExtraTimeAdded`, now titled "Time was added or taken away") instead of a new rule, so the owner's existing switch carries over. Take-aways are announced only from the plugin's change list, because the credit also drops on resets.
- Still to do: HA edits (needs the chore label table approved), emulator/live check, app release (needs "publish it"), plugin tag (needs approval).

## Not yet verified
Regression suite and a live test of the plugin on the test Emby server; the push (websocket) carrying the new field.


## Push versus polling (checked 2026-11-03, owner asked)
- Confirmed by reading both codebases: the app does not poll while connected. The plugin's `StatusPushListener` pushes the whole status over Emby's websocket on every change (`SaveWatchTimeDataImmediate` calls `NotifyChanged`, which every extend/reduce/time-out/reset goes through), after playback events (coalesced, 800 ms), and as a 30 s heartbeat. The app's `LiveConnection` receives it and `AlertService` feeds it to `HallMonitorEngine`.
- The only REST fetches are the first load, pull-to-refresh, a refresh after the app's own actions, and a 20 s fallback that runs only while the live connection is NOT live (`HallMonitorRepository.FALLBACK_POLL_MS`). The backup poll job explicitly skips Hall Monitor.
- The new change list rides on that same push (`GetLimitedUsersStatus()` fills `RecentTimeChanges`; the change is recorded before the save that triggers the push), so the reason arrives with the same immediacy. No polling was added. A new test checks that a pushed status parses with the list.

## Live test (2026-11-03, test server `C:\Users\kgiglio\Downloads\embyserver-win-x64-4.10.1.0`)
- I started that server myself (it was not running), installed plugin 2.5.0.0 over 2.4.0.0 (old DLL saved in the session scratchpad), ran everything, then stopped only the process I started. The test server keeps 2.5.0.0 installed; throw-away users and plugin settings were removed.
- Regression suite: 74 of 74. New script `tools/Test-ReasonLive.ps1` (12 checks): the push arrives right after ExtendTime and carries the reason; all combinations of Source/Reason behave as specified; credit unchanged; a bad request is still refused.
- App: a real pushed status captured from that run is a test resource; the app's parser and engine produce the expected text.
- Not done: emulator run (notification seen on screen); HA change not pushed or tried in HA.
- Heads-up for the owner: HA sends `user_id: "Harper"` (a name), but the plugin matches only the user's Emby id (`ResolveListedUser`). It must already work for you (the Limited Users entry's id must match), but if chore adds ever do nothing, check that first.
