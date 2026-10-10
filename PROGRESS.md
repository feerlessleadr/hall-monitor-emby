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

## 7. GitHub backup
2026-10-09: pushed to https://github.com/feerlessleadr/watching-eye-emby.git (branch `main` plus the three tags). The pushed history is a rewritten copy of the original local history: all commits are attributed to `feerlessleadr <github@thecgkg.com>` (no work email or username anywhere in metadata, messages or files) and `original/WatchingEye.dll` was removed from every commit (the README keeps its SHA-256).
The same day this local repository was **switched to the published history** (`origin/main`), so local and GitHub now share the same commit hashes. Local git identity is set to the same author.
Kept locally only (not published): the pre-switch history as `C:\dev\watching-eye-original-history.bundle` (contains the original DLL and the work email - do not publish), and the original binary at `original/WatchingEye.dll` (ignored by git) with a copy in `C:\dev\watching-eye-original-binary\`.

## 8. Android app compatibility (2026-10-09)
Companion app: https://github.com/feerlessleadr/watching-eye-android-app (built by the user with another AI tool; version 1.2 in the supplied APK, same classes and routes as the repo).
**Review result:** the app calls six plugin routes (`LimitedUsersStatus`, `Public/Status`, `Public/ExtendTime`, `Public/TimeOutUser`, `Public/ClearTimeOut`, `Public/ResetUserTime`) plus three Emby routes. None were removed. Verified by replaying the app requests against the plugin.
**Problems found and fixed on the plugin side (commit 78e59b6, pushed):**
- User names with quotes/ampersands were altered by the sanitiser (app could not match the playing session): names are now kept exactly (`LogSanitizer.CleanText`).
- Expired time-outs were still reported as active: `TimeOutUntil` is now 0001-01-01 once a time-out has ended.
- Status gained two optional fields: `EffectiveDailyLimitMinutes` (honours per-day limits) and `IsEnabled`.
**Fixed on the app side (app repo v1.3, commit 0d6666d, pushed):** credentials moved from the URL (`api_key=`) to headers (`X-Emby-Token`) so Emby no longer logs the admin key; local-time time-outs hidden when expired; minutes clamped to 1..43200 with plain-language error messages; the public route is not retried for 5 minutes after a 403 so a wrong plugin key cannot trigger the plugin lockout; http:// warning; stale manifest entry removed; 9 unit tests plus an opt-in integration test (4 checks, all pass against this plugin); decompiled `export.zip` and extracted artifacts removed from the repo.
**Needed by the app (setup, not a defect):** the plugin Enable Public API setting must be ticked and a plugin key saved (it is now enforced); the Emby key must be an administrator key; the server address must be https:// (the app blocks plain http://).
**Not verified:** the UI on a real phone or emulator (the app compiles and its logic is tested, but screens were not viewed). The debug-signed APK built here (`C:\dev\WatchingEye-v1.3-debug.apk`) cannot be installed over the original release build because the signing key differs: uninstall the old app first (this clears its saved settings) or sign with the original key.
**Build setup used:** Android Studio JBR (Java 25) as JAVA_HOME, Gradle 9.3.0 wrapper, AGP 8.10.1, compileSdk 35; first build downloaded Gradle and the Android build components. Working clone: `C:\dev\watching-eye-android-app`.

## 9. Android app: signed release automation (2026-10-09)
App repo commit 656cb4b adds GitHub Actions: `ci.yml` (tests + debug build on every push, no secrets) and `release.yml` (on a `v*` tag, or a manual dry run: tests, signed release APK, certificate fingerprint check, GitHub Release with APK and checksum). Release signing in `app/build.gradle` reads `ANDROID_KEYSTORE_FILE` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASSWORD`; with none set the release build stays unsigned. Actions are pinned to commit hashes.
The user confirmed the keystore (`C:\androidkeys\watchingeye.jks`, alias `key0`) is the key of the installed app: its SHA-256 fingerprint `DF:15:A0:B3:A7:31:31:32:6A:10:B5:49:34:D1:EE:A3:C2:A0:24:95:19:D2:28:31:2E:C3:72:61:85:F4:36:F7` equals the signature of the supplied APK. The keystore and passwords were never shared with the assistant; the user adds them as four repository secrets (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`).
Tested locally with a throwaway key (since deleted): unsigned build without a key, signed build with the same environment variables the workflow sets (including the base64 round trip), fingerprint guard passes for the right key / rejects a wrong key / rejects an unsigned APK / accepts the user's real APK against the real fingerprint, tag-vs-version check, YAML validity. Not testable here: the workflow run on GitHub itself (needs the secrets), so the first dry run may need a small fix.
Note for later: a test technique trap on this machine - `grep -c $'\r'` is passed as an empty pattern and matches every line; count carriage returns with `tr -cd '\r' | wc -c` instead (always run a control).
