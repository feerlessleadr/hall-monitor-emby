# PROGRESS

## Done
- [x] Original preserved: `original/WatchingEye.dll` (read-only; identical hash to the source file in the folder root, which is git-ignored).
- [x] Recon: not obfuscated (clean names, no control-flow mangling). net6.0, AssemblyVersion 1.6.8.0.
- [x] Decompiled with `ilspycmd -p` -> `src/WatchingEye` (commit 1, untouched output).
- [x] csproj fixed (commit 2): `MediaBrowser.Server.Core` 4.9.1.90 from NuGet covers all references (Model/Common/Controller 4.9.1.90), so no Emby install DLLs are needed. RootNamespace set, LangVersion latest. **Builds with 0 errors, 0 warnings on first try**; no async/goto/dynamic fixes were needed.
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
