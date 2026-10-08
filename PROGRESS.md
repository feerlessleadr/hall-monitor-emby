# PROGRESS

## Done
- [x] Original preserved: `original/WatchingEye.dll` (read-only; identical hash to the source file in the folder root, which is git-ignored).
- [x] Recon: not obfuscated (clean names, no control-flow mangling). net6.0, AssemblyVersion 1.6.8.0.
- [x] Decompiled with `ilspycmd -p` -> `src/WatchingEye` (commit 1, untouched output).
- [x] csproj fixed (commit 2): `MediaBrowser.Server.Core` 4.9.1.90 from NuGet covers all references (Model/Common/Controller 4.9.1.90), so no Emby install DLLs are needed. RootNamespace set, LangVersion latest. **Builds with 0 errors on first try** (1 CS4014 un-awaited-call warning at TranscodeMonitor.cs:348, original fire-and-forget behavior); no async/goto/dynamic fixes were needed.
- [x] Verified vs original (reflection via MetadataLoadContext, all members incl. non-public): 920-line dumps identical except one compiler-generated lambda name (`<Start>b__9_0` vs `b__10_0`). Same 3 embedded resources with identical byte sizes, same GUID, name, version.
- [x] Cleanup commits: removed `//IL_xxxx` comments; renamed decompiler-style locals.

## Left / ideas
- Not yet test-loaded into a real Emby server (build-level equivalence only).
- Remaining decompiler style: `((BasePlugin<PluginConfiguration>)(object)Instance).Configuration` casts and `Array.Empty<object>()` logger args; harmless, left as is.
- Behavior changes: none made. Awaiting your direction.

## Findings / flags
1. **No outbound network calls, telemetry, license checks or obfuscated blobs.** No HttpClient/WebClient/Process/Assembly.Load/Base64 anywhere; the only URL strings are docs text in the config page.
2. **`ExternalWebServer` is dead code.** It would open an `HttpListener` on `http://*:{port}/` with a password login and token API (`/api/login`, `/api/extend`, ...), but nothing instantiates it and the `WatchingEye.Web.*` resources it serves don't exist. Left in for fidelity; a candidate for removal (your call).
3. **Admin routes have no auth attribute.** `WatchTimeService` routes (`/WatchingEye/ExtendTime`, `TimeOutUser`, `ResetUserTime`, `ClearLogs`, `DeleteUserData`, ...) carry no `[Authenticated(Roles="Admin")]`. Emby's default then lets any authenticated user call them, so a limited user's own token could extend their time. Worth verifying and fixing; this is a behavior change so I have not touched it.
4. Public API uses a shared key in `X-WatchingEye-Api-Key`, compared with plain `string.Equals` (not constant-time); disabled when the key is blank. Key is stored in plugin config.

## What the plugin does
**Watch-time limiter** (`WatchTimeManager`): timer-driven tracking of per-user watch ticks (daily/weekly/monthly/yearly), limits per user plus a global rule set, allowed time-of-day windows, per-day overrides, per-library time restrictions, time credits ("extend"), manual time-outs, optional daily/weekly/monthly/yearly auto-reset, threshold notifications (% used), excluded users/clients/libraries. When exceeded: stops playback and shows an in-app message. State in `WatchingEye.WatchTime.json` (plugin configurations dir; atomic temp+replace writes).
**Transcode monitor** (`TranscodeMonitor`, 30 s session check): in-client messages when transcoding (reason parsed into client-limitation vs bandwidth-limitation text), optional direct-play and playback-start messages, blocking by source container (default mkv,ts,avi), by max transcode resolution, by concurrent transcode count, stopping audio/video transcodes, paused-stream timeout. Optional confirmation button on each message type.
**Other**: Event log (`LogManager`, persisted JSON, viewable/clearable from config page), server notifications through Emby's notification manager (limit reached, thresholds), config page + logo embedded.
**Scheduled tasks**: none (internal `Timer`s only; no `IScheduledTask`).
**Routes** (`/WatchingEye/...`): ExtendTime, LimitedUsersStatus, ToggleUserLimit, ResetUserTime, ResetAllUsersTime, GetLogEvents, ClearLogs, TimeOutUser, ClearTimeOut, GetClientList, DeleteUserData, ExtendPeriodTime, ResetPeriodTime; API-key protected: `Public/Status` (GET), `Public/ExtendTime`, `Public/TimeOutUser`, `Public/ClearTimeOut`, `Public/ResetUserTime` (POST). Documented in the config page's "External API Guide".
**Config** (`PluginConfiguration`, saved as `WatchingEye.xml`): ~50 options, grouped as on the config page (transcode notification/blocking, resolution, concurrency, direct play, playback start, paused timeout, message texts, confirmation buttons, exclusions, limiter + `LimitedUsers`, `GlobalLimitedUser`, `LibraryTimeRestrictions`, `ApiKey`, `EnablePublicApi`). Property names are serialized, do not rename.

## Verification tooling
Throwaway MetadataLoadContext dumper lived in the session scratchpad (not in repo). Re-do the diff by dumping all members of the original and rebuilt DLLs and diffing.

## Test run on Emby 4.10.1.0 (unmodified rebuild, 2026-10-08)
Rebuilt Release DLL copied to the test server's `programdata/plugins`. Results:
- PASS: loads, registered via `/Plugins` as Watching Eye 1.6.8.0 / GUID e8c3b1b3-..., entry point starts, no errors. (Built against 4.9.1.90, runs on 4.10.1.0 / .NET 8.)
- PASS: embedded config page + js served; anonymous calls to plugin routes -> 401.
- PASS: limiter, using a simulated client (Sessions/Playing + Progress reports) with a 1-minute daily limit. Watch time accrues at 15 s per tick, `LimitReached` event logged at ~60 s, shows in `GetLogEvents`. Config backup/restore through the Plugins API works.
- NOT TESTED: config page UI rendering/saving in a browser, transcode warnings/blocking, in-client messages (the simulated client has no websocket, so message delivery and the actual stop command were not observed).
- **CONFIRMED vulnerability (finding 3):** a freshly created non-admin user's token got 200/204 on `LimitedUsersStatus`, `GetLogEvents`, `GetClientList`, `ExtendTime`, `TimeOutUser`, `ClearTimeOut` and `ClearLogs`, while Emby's own `/Plugins` returned 403 for that user. The probe's `ExtendTime` really granted a 300 s credit (which is why the first enforcement run didn't stop). Fix = `[Authenticated(Roles = "Admin")]` on the non-public request classes; not applied (behavior change, awaiting approval).
- Test hygiene: probe user deleted, plugin config restored to defaults, plugin log cleared. Plugin left installed on the test server.

## Fix applied: admin-only routes (approved by user)
Added `[Authenticated(Roles = "Admin")]` to the 13 non-public request classes in `WatchingEye.Services`. Re-tested on Emby 4.10.1.0 with a fresh non-admin user: all 13 routes -> 403; admin (API key) -> 200/204. `Public/*` routes unchanged (API-key auth). Note: `Public/Status` called with a non-matching/absent key as a logged-in user returns 500 (it throws UnauthorizedAccessException, which Emby maps to 500); this is pre-existing behavior, not changed.

## Remaining tests completed (Emby 4.10.1.0, build with admin-route fix)
- PASS save from config page: toggled a setting in the dashboard UI, "Settings saved." toast, value persisted in `WatchingEye.xml` and via the plugin Configuration API.
- PASS message + stop delivery over a real websocket client: 50% threshold warning (`DisplayMessage`) at 30 s, then at the 60 s limit `Playstate: Stop` + "Time Limit Reached" `DisplayMessage`; the Stop is re-sent every 15 s while the client keeps reporting playback.
- PASS direct-play and playback-start messages delivered.
- PASS transcode warning with a **real client** (Emby Web in the app browser, 720p MPEG-4 MKV forced to transcode by Chromium): event "Transcode started: the video format is not supported" logged and the "Transcode Warning" dialog displayed with the configured text.
- PASS transcode blocking decision (`EnableTranscodeBlocking`, mkv in the blocklist): server log "Blocking transcode ... Source format 'mkv' is on the blocklist"; the "Playback Blocked" `DisplayMessage` and `Playstate: Stop` were delivered to the client's websocket (captured in the page). **Caveat:** the in-pane Emby Web player did not act on the Stop (nor on Emby's own `Sessions/{id}/Playing/Stop`), so end-to-end "playback actually stops" was not observed for web; the plugin side is verified. Stop is only a request to the client; a hardening idea is also killing the transcode/session server-side.
- NOT TESTED: resolution blocking, concurrent-transcode limit, paused-stream timeout, library time restrictions, time windows, external Public API with a real key.

### Fragility found (original behavior, unchanged)
`TranscodeMonitor.HandleMediaStatusNotification` checks `session.TranscodingInfo == null` at the instant playback-start is reported and, if null, treats the stream as direct play and returns without re-checking (the `InitialDelaySeconds` wait only happens when TI is already non-null). Scripted clients that report start before Emby attaches TranscodingInfo get no warning (or a false "Direct Play" message); real Emby Web attached it in time. Candidate improvement: re-read the session after the delay before deciding. Behavior change, not applied.

### Test hygiene
Probe users deleted, plugin config restored to defaults, plugin log and orphan watch-time data cleared, generated 288 MB test clip removed from `C:\claude\Movies` and library refreshed, helper websocket process stopped. Plugin and server left running.
