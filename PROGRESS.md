# PROGRESS - Watching Eye (recovered fork)

Living log for this fork. Sections: current state, decision log, issues and workarounds, test history, backlog.
Detailed change history is in the git log (every behaviour change is its own commit).
Last updated: 2026-10-09.

## 1. Current state

**What it is:** an Emby Server plugin (Emby 4.9/4.10, net6.0) that limits how long selected users can watch.
Recovered by decompiling the original `WatchingEye.dll` v1.6.8.0 (original author unknown; GitHub repo deleted).
Plugin GUID `e8c3b1b3-4f56-4f38-a28a-2e6c5a043007` and name "Watching Eye" are unchanged, so Emby treats it as the same plugin.

**What it does now**
- Per-user limits (only users listed under *Limited Users* are ever affected): daily / weekly / monthly / yearly caps, per-day limits, allowed time windows, resets, threshold warnings.
- Extra time ("Extend") per user or per period; time-outs (stop and block until a local date/time).
- Stops playback when a limit is reached (Stop command + message) and blocks new playback while over limit / timed out / outside the window.
- Event log, optional server notifications, optional API-key control (`/WatchingEye/Public/*`).
- Enabled limits of 0 are rejected on save (HTTP 400 / UI message); 0 never means "unlimited".

**Removed from the original** (all at the user's request unless noted): transcode warnings/blocking/direct-play messages, playback-start message, paused-stream timeout, global rules, exclusions (users/clients/libraries), library time restrictions, dead code (embedded web server, duplicate notification service, unused routes).

**Build:** `dotnet build src/WatchingEye -c Release` -> `src/WatchingEye/bin/Release/net6.0/WatchingEye.dll`; copy that one file to Emby's `plugins` folder and restart. Dependency: NuGet `MediaBrowser.Server.Core` 4.9.1.90 (no Emby DLLs needed).
**Tags:** `v1.6.8.0-recovered` (faithful rebuild), `v1.6.8.0-watchtime-only`, `v1.6.8.0-hardened`.
**Status:** 34/34 regression checks pass on Emby 4.10.1.0 (`tools/Run-RegressionTests.ps1`, see `TESTING.md`). Not yet installed on the user's real server.

## 2. Decision log

| Date | Decision | By | Why / notes |
|------|----------|----|-------------|
| 10-08 | Keep original DLL untouched in `original/`; commit raw decompile first | user's brief | Lets anyone diff raw decompile vs. later changes |
| 10-08 | Use NuGet `MediaBrowser.Server.Core` 4.9.1.90 instead of Emby install DLLs | assistant | Exact version match; builds with 0 errors; nothing copied from the server |
| 10-08 | Rebuilt DLL verified against original: same types/members/resources/GUID | assistant | Only compiler-generated lambda names differ |
| 10-08 | Admin-only routes (`[Authenticated(Roles="Admin")]` on 12 routes) | user approved | Any logged-in user could extend time / clear logs |
| 10-08 | Remove transcode features | user | Only watch-time limiting matters |
| 10-08 | Remove playback-start message + paused-stream timeout | user | Not about watch time |
| 10-08 | Time-out message shows local end date/time (`{end_time}`) | user | Server local time; `{duration}` still works |
| 10-08 | Remove global rules; only listed users are affected | user | Rules are applied per account |
| 10-08 | Remove exclusions; time-out refuses unlisted users | user | Exclusions redundant; unenforceable time-outs were misleading |
| 10-08 | Time-out also refuses when the limiter is disabled | assistant | Same reason: it could not be enforced |
| 10-09 | Remove library restrictions | user | Only per-user watch time, extra time and time-outs wanted |
| 10-09 | Remove dead code | user | Embedded web server was never started |
| 10-09 | Security hardening (see section 5) | user: "make all fixes" | After full review |
| 10-09 | Chosen limits: minutes 1..43200 per request, credit cap 10 years, API lockout 5 failures/5 min -> 5 min, save every 60 s | assistant | Conservative defaults; easy to change |
| 10-09 | Enabled limit of 0 is rejected (option 2) | user | A blank field must not silently disable a limit |
| 10-09 | Kept legacy data migration (`WatchedTimeTicks` -> daily) in the loader | assistant | Harmless; only matters when upgrading very old data files |

## 3. Issues and workarounds log

| Issue | Cause / workaround |
|-------|--------------------|
| PowerShell scripts blocked | Execution policy. Run with `powershell -ExecutionPolicy Bypass -File ...` (process-only, changes no setting) |
| A path guard rejected a script containing single-letter string arguments | Use descriptive ids (e.g. `'phase1'`, not `'a'`) and `-LiteralPath` |
| Variables clobbered (`$s` overwrote `$S`) | PowerShell variables are case-insensitive; use distinct names |
| Multi-line `sed` inserts corrupted code (twice) | Use the editor tool or here-doc files for multi-line edits; rebuild after every edit |
| No Python/Node on the machine | JS syntax check via `new Function(source)` in the app browser |
| Config page showed stale HTML after plugin update | Emby Web caches plugin pages. Load with a new query string (`?fresh=N`) or `fetch(url,{cache:'reload'})` |
| `location.reload()` did not reload in the app browser pane | Navigate to a URL with a new query string instead |
| Emby Web in the app browser ignored the Stop command (also Emby's own admin Stop) | Client behaviour. Plugin side verified via the websocket client; see limitations |
| Scripted clients: `TranscodingInfo` not attached at playback start | Only affected the removed transcode feature |
| Emby's JSON parser accepted a truncated data file without error | Added a strict JSON validity check before loading; corrupt files are moved aside as `.corrupt-<time>` |
| Emby HTML-encodes the client name only when it arrives via `X-Emby-Authorization`; the query-string / `X-Emby-Client` paths are stored raw | The plugin sanitises names itself and the page escapes all output |
| A safety check blocked saving a detailed vulnerability write-up | Only a defensive summary is kept (section 5); do not retry |
| Test server log still contains tokens/lines from testing | Emby logs tokens in request URLs; test users are deleted so tokens are dead |
| `PlaybackProgress` simulation: websocket receive cancelled -> socket aborted | `tools/ws-listen.ps1` blocks on receive instead |

## 4. Test history

- 10-08: rebuilt DLL loads on Emby 4.10.1.0; config page served; limiter enforcement; websocket delivery of Stop / DisplayMessage; real-client (Emby Web) transcode warning/blocking decision (since removed).
- 10-08/09: after each behaviour change: load, UI render, save round-trip, enforcement, time-out message with end time.
- 10-09: security review + hardening verified (see section 5); regression suite added: **34/34 pass**.
- How to repeat everything: `TESTING.md`.

## 5. Security review summary (defensive)

Reviewed: all C#, the config page (HTML + JS), csproj, embedded logo, NuGet dependency, git history, decompilation provenance; live testing on a test server; plus an independent read-only review.
**Fixed:** unescaped data in the admin page and unsanitised client names; Public API enable flag not enforced, key comparison, failure throttling, key generation, rejection status codes; unbounded / unvalidated minutes and user ids; integer overflow of time credit; user-id casing mismatch; log-line forging via ids; one invalid config value halting enforcement for all users; unreadable data file silently overwritten; zero limits meaning "unlimited"; unnecessary build permissions (unsafe code, no overflow checks).
**Checked and clean:** admin routes require the Admin role; no network / process / reflection / unsafe code; file I/O limited to two files in the plugin folder; no secrets in repo or history; NuGet package has no known vulnerabilities; logo is a valid JPEG with no hidden payload.
**Known limitations (by design):** enforcement relies on Emby session reports and a Stop command, so a client that ignores Stop or does not report playback is not stopped; time is sampled every 15 s; the API key is stored in plain text in the plugin config; Emby itself logs session tokens that appear in URLs.

## 6. Backlog / ideas

- Install on the real server (build Release DLL, back up existing plugin config, copy DLL, restart).
- Optionally hash the stored API key; optionally add an audit trail for admin actions (extend / reset / time-out).
- Consider limiting the Public API to specific client addresses.
- Add the regression suite to CI if the repo gets one.
