# Hall Monitor: decision log

Append-only, newest at the bottom. Never rewrite an entry; if a decision is reversed, add a new entry that references the old one. This file starts the persistent-docs log. The full history before 2026-11-03 lives in `PROGRESS.md` (section 2 has a dated decision table) and `docs/reason-for-time-changes.md` (every 2.5.0.0 decision). Do not duplicate those here; add pointers.

### 2026-10-08, Rewrite a decompiled plugin, keep the original out of the repo
- **Context:** The plugin was recovered by decompiling WatchingEye 1.6.8.0 (author unknown, repository deleted).
- **Options considered:** Publish the decompile as is; rewrite it; drop the idea.
- **Decision:** Commit the raw decompile first, then rewrite by hand. The original DLL stays untouched in `original/` (git-ignored) and is never published. No LICENSE file; the README carries a provenance notice and a takedown contact.
- **Rationale:** Nobody can grant a license for code derived from an unknown author's work. Keeping the original out of GitHub limits the risk.
- **Impact:** `README.md`, `.gitignore`, `PROGRESS.md` section 11; `C:\dev\watching-eye-original-binary`. The plugin GUID stays the same so Emby treats it as the same plugin.

### 2026-10-08, Admin-only routes and a smaller feature set
- **Context:** Any logged-in user could extend time or clear logs.
- **Options considered:** Leave as is; restrict to admins.
- **Decision:** Admin routes use `[Authenticated(Roles="Admin")]`. Only listed users are affected. Transcode features, playback-start messages, paused-stream timeout, global rules, exclusions and library restrictions were removed.
- **Rationale:** Only per-user watch time, extra time and time-outs are wanted; unenforceable features were misleading.
- **Impact:** All clients. Details in `PROGRESS.md` section 2.

### 2026-10-10, Live push over the Emby websocket, plugin key inside the message
- **Context:** The app polled every 10 seconds.
- **Options considered:** Keep polling; push status over Emby's websocket.
- **Decision:** `StatusPushListener` pushes status on every change plus a 30 second heartbeat. Authorization is an administrator login, or the plugin API key with the Public API enabled. The key travels in the websocket message, never in a URL.
- **Rationale:** Removes polling and keeps the key out of logs and addresses. Status data is as sensitive as the public routes.
- **Impact:** Embidextrous app (REST kept only as a fallback while the socket is down); same lockout as the HTTP routes via `ApiKeyGate`.

### 2026-10-12, Everything renamed to Hall Monitor (2.0.0.0)
- **Context:** Plugin first became watching-eye-emby, then Hall Monitor to pair with Hall Pass.
- **Options considered:** Keep old route names for compatibility; rename everything.
- **Decision:** DLL `HallMonitor.dll`, routes `/emby/HallMonitor/...`, header `X-HallMonitor-Api-Key`, socket `HallMonitorStatus`. Old names are no longer answered. First start of 2.0 migrates old settings.
- **Rationale:** One clean set of names, fixed for good.
- **Impact:** Old Watching Eye apps stop working; Embidextrous 0.5 or later is required. Docs in other repos that call the plugin `watching-eye` are stale.

### 2026-10-15, A limit of 0 means no time left (2.4.0.0)
- **Context:** An earlier decision (2026-10-09) rejected a limit of 0 so a blank field could not silently disable a limit.
- **Options considered:** Keep rejecting 0; treat 0 as a real limit.
- **Decision:** 0 is a real limit meaning no time left. A blank field is blocked by validation instead. This supersedes the 2026-10-09 rule in `PROGRESS.md` section 2.
- **Rationale:** Parents need a way to say "no time today".
- **Impact:** `EffectiveDailyLimitMinutes` is 0 for both "no limit" and "limit 0", so clients must read the `Limits` list.

### 2026-11-03, Say why time changed (2.5.0.0, ApiLevel 7)
- **Context:** Home Assistant chore awards and manual additions all looked the same in the app's notifications.
- **Options considered:** Notify without reasons; send a reason from each caller.
- **Decision:** Optional `Source` and `Reason` on the extend and reduce routes, an in-memory log of the latest 20 changes per user, and `RecentTimeChanges` in status. All additive. Full decision list: `docs/reason-for-time-changes.md`.
- **Rationale:** Old clients keep working; the app can word notifications by cause and fall back to today's wording.
- **Impact:** Embidextrous (consumer, notification wording), `C:\dev\ha` (sender, two local commits not pushed), `C:\dev\_docs\WORKSPACE.md`. Deployment order: plugin, then Home Assistant, then app.

### 2026-11-03, hall-monitor-emby is the live source; watching-eye is deprecated
- **Context:** Two local clones share one GitHub remote, so work could land in the wrong one.
- **Options considered:** Keep both active; mark one deprecated.
- **Decision:** `C:\dev\hall-monitor-emby` is canonical (owner's decision recorded in `docs/reason-for-time-changes.md`). `C:\dev\watching-eye` is deprecated and must not be edited.
- **Rationale:** Avoid divergence. The clones already diverged: origin has 2.4.1.0 that this clone lacks.
- **Impact:** Integrate origin's 2.4.1.0 here before any push or release. Other repos' docs that cite `C:\dev\watching-eye` as the Hall Monitor reference are stale.

### 2026-11-03, The change list lives in memory only
- **Context:** The app only needs to explain a credit rise or drop that just happened. A permanent record would be a second ledger to keep correct.
- **Options considered:** Persist changes to the plugin's data file; keep a bounded in-memory list.
- **Decision:** `TimeChangeLog` keeps the latest 20 changes per user in memory. Ids are seeded from the clock so ids from before a restart are always smaller than ids after it. `Source` is normalised to app, plugin, homeassistant, other (unrecognised) or unknown (missing). `Reason` is one line, control characters removed, at most 80 characters. The change is recorded before the save that triggers the websocket push, so the reason travels in the same push as the new time.
- **Rationale:** After an Emby restart the list is empty and the app falls back to its old wording, which is harmless. No new file format, no migration.
- **Impact:** The app must treat `RecentTimeChanges` as optional (null on plugins before 2.5.0.0). The plugin dashboard does not show reasons. Do not add a consumer that expects the list to survive a restart.

### 2026-11-03, Merge origin's 2.4.1.0 into 2.5.0.0 instead of rebasing
- **Context:** Local `main` held four unpushed 2.5.0.0 commits and origin held the 2.4.1.0 fix (due resets applied at start). Releasing from local alone would have shipped 2.5.0.0 without that fix and the app would again announce yesterday's usage after a restart.
- **Options considered:** Rebase local onto origin; merge origin into local.
- **Decision:** Merge. Two conflicts: `AssemblyInfo.cs` (kept 2.5.0.0) and `PROGRESS.md` (both sections kept; the reasons section renumbered from 20 to 21). Pushed and tagged `v2.5.0.0` on a7c7c9a after CodeQL (csharp, actions, javascript-typescript) was green.
- **Rationale:** A merge keeps already-pushed commit ids intact and was the smaller risk. The owner said "publish everything", which covered the integration.
- **Impact:** Released `HallMonitor.dll` is 232,960 bytes, SHA-256 `6ef6470b53a0862b1759efaf30b1e165cfe0abab2724abce1f8305100c08e247`. The merged build was built and unit tested (33 tests) but NOT re-run against the live test server; the live results (regression 74 of 74, reason script 12 of 12) were from the pre-merge tree. `C:\dev\watching-eye` stays deprecated.

### 2026-11-03, Surprising discovery: the plugin matches an Emby user id, Home Assistant sends a name
- **Context:** The first live run of the reason script failed ("user is not in the Limited Users list") because the script passed the user's name. `ResolveListedUser` matches only the Emby user id stored in `LimitedUsers`. Home Assistant's chore automations send `user_id: "Harper"` and similar names.
- **Options considered:** Make the plugin also match by name; leave it and confirm the live setup works.
- **Decision:** No plugin change in this release. The owner says chore awards already work, so the Limited Users entries must carry whatever HA sends. Not re-verified.
- **Rationale:** Changing user resolution is a behaviour change outside the reasons feature.
- **Impact:** If a chore award ever does nothing, check the Limited Users entry's id against what HA sends first. Recorded as an open question in `STATUS.md`.
