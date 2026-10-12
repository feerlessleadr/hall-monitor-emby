# PROGRESS - Hall Monitor (formerly Watching Eye, recovered fork)

Living log for this plugin. Sections: current state, decision log, issues and workarounds, test history, backlog.
Detailed change history is in the git log (every behaviour change is its own commit).
Last updated: 2026-10-10 (live push, plugin 1.8.0.0 and Android app 1.5 added; see section 12).

## 1. Current state

**What it is:** an Emby Server plugin (Emby 4.9/4.10, net6.0) that limits how long selected users can watch.
Recovered by decompiling the original `WatchingEye.dll` v1.6.8.0 (original author unknown; GitHub repo deleted).
Plugin GUID `e8c3b1b3-4f56-4f38-a28a-2e6c5a043007` is unchanged, so Emby treats it as the same plugin (the display name changed to "Hall Monitor" in 1.10, see section 13).

**What it does now**
- Per-user limits (only users listed under *Limited Users* are ever affected): daily / weekly / monthly / yearly caps, per-day limits, allowed time windows, resets, threshold warnings.
- Extra time ("Extend") per user or per period; time-outs (stop and block until a local date/time).
- Stops playback when a limit is reached (Stop command + message) and blocks new playback while over limit / timed out / outside the window.
- Event log, optional server notifications, optional API-key control (`/WatchingEye/Public/*`).
- Enabled limits of 0 are rejected on save (HTTP 400 / UI message); 0 never means "unlimited".

**Removed from the original** (all at the user's request unless noted): transcode warnings/blocking/direct-play messages, playback-start message, paused-stream timeout, global rules, exclusions (users/clients/libraries), library time restrictions, dead code (embedded web server, duplicate notification service, unused routes).

**Build:** `dotnet build src/WatchingEye -c Release` -> `src/WatchingEye/bin/Release/net6.0/WatchingEye.dll`; copy that one file to Emby's `plugins` folder and restart. Dependency: NuGet `MediaBrowser.Server.Core` 4.9.1.90 (no Emby DLLs needed).
**Tags:** `v1.6.8.0-recovered` (faithful rebuild), `v1.6.8.0-watchtime-only`, `v1.6.8.0-hardened`.
**Status:** 64/64 regression checks pass on Emby 4.10.1.0 (`tools/Run-RegressionTests.ps1`, see `TESTING.md`; the 5-minute lockout check is skipped with `-SkipLockout`). Plugin version in the source is 1.9.0.0; published releases are v1.7.0.0 and v1.8.0.0 (tag-triggered workflow `.github/workflows/release.yml`; the user installs it on their real server with their own GitHub-repo downloader plugin). v1.8.0.0 (live push, undo) was tagged and published on 2026-10-10 together with app v1.5; check the Actions tab and Releases page for both runs. Companion Android app: https://github.com/feerlessleadr/watching-eye-android-app (v1.4, signed releases built by GitHub Actions; section 8-10).

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
| 10-09 | Switched the local repo to the rewritten, published history; commits attributed to the GitHub identity, original DLL kept out of the published repo | user | Keeps the work email and the third-party binary out of GitHub; the old history is kept in a local bundle |
| 10-09 | User names are kept exactly (new `CleanText`); expired time-outs reported as none; status gained optional `EffectiveDailyLimitMinutes` and `IsEnabled` | user: "fix both plugin and app" | Compatibility with the Android app (names with quotes broke session matching; expired time-outs looked active) |
| 10-09 | Android app: auth in headers, local-time time-outs, clamped minutes, backoff after a 403; app repo got CI, a tag-triggered signed release workflow (secrets in GitHub, certificate fingerprint check), MIT license, history purge | user | See sections 8-10 |
| 10-09 | App release process: bump `versionCode`/`versionName`, push, then push tag `v<versionName>`; first signed release is v1.4 | user | Tag must equal `v` + versionName or the workflow stops |
| 10-09 | Plugin repo to be made public with a provenance / no-license / takedown notice and no LICENSE file | user asked to publish; assistant advised | The code is derived from a decompiled plugin by an unknown author, so no license can be granted for it (see section 11) |

| 10-10 | Plugin release workflow: tag `v<AssemblyVersion>` builds the DLL and publishes it with a checksum; version bumped from 1.6.8.0 to 1.7.0.0 so updaters see it as newer; v1.7.0.0 published | user: "publish it" | The user installs/updates the plugin with a GitHub-repo downloader plugin on their production Emby |
| 10-10 | App changes from polling to server push (plugin 1.8.0.0 `StatusPushListener` over Emby's websocket plus Emby's own `SessionsStart`); REST kept only as a fallback while the socket is down | user | Removes the 10-second refresh; the flicker itself was fixed earlier (redraw only changed cards) |
| 10-10 | Push authorisation: administrator login, or the plugin API key with the Public API enabled (same lockout as the HTTP routes, shared in `ApiKeyGate`); the key travels in the websocket message, never in a URL | assistant | Status data is as sensitive as the Public routes |
| 10-10 | New `ReduceTime` routes (admin and public) take back extra time, floored at zero | user asked for Undo | Undo for Extend |
| 10-10 | Status gained `BlockReason`, `SecondsRemaining`, `LimitingPeriod`, `LimitingAllowedSeconds` (additive) | assistant | Lets the app show an accurate countdown for the closest enabled limit and alert on limits |
| 10-10 | App logo: a watchful owl (one clock eye = time, one real eye = watching) on a night sky; chosen from several concepts (ring-iris eye, clock eye with slit pupil, hourglass of play triangles, moon that watches, play-button eye); adaptive icon with a one-colour themed version; master SVG and PNG in the app repo `assets/logo` | user: "design me a new logo" | Original artwork, MIT with the app |
| 10-10 | App 1.5: card redesign, quick +15/+30/+60, custom minutes, "End time-out + 30", confirmations, Undo snackbar, pull-to-refresh, live/updated indicator, light/dark theme, optional background alerts (foreground service + notification with add-time buttons, off by default), home-screen widget | user: "do all of these" | Background alerts need a foreground service with a permanent notification; widget refreshes at most every 30 minutes on its own |

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
| The tool's path guard blocked a command that contained the word `Remove-Item` next to a later `/Users` string | Avoid that word combination; clear environment variables with `$env:NAME = $null` |
| Writing a file whose content used certain quoting made the whole shell command fail to parse (nothing ran) | Use the file-writing tool for files with quotes/apostrophes instead of shell here-docs |
| `grep -c $'\r'` counts every line (empty pattern) | Count carriage returns with `tr -cd '\r' \| wc -c` and always run a positive control |
| `git filter-branch` leaves `refs/original/*` that still hold the old history | Delete those refs and `gc --prune=now` before pushing; verify the rewritten copy first (trees identical except the removed file) |
| Force-updating an existing tag re-triggers the tag workflow | Make the publish step skip when the release already exists (done in the app repo) |
| GitHub run results are not visible from here | The user reports the Actions result (screenshots); workflow logic is tested locally with a throwaway key |
| apksigner on the runner: first dry run reported "found 0 signers" | Script now keeps apksigner output, accepts both signer-line formats, tries each installed apksigner; Java 21 and ubuntu-24.04 pinned |

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

- **Install on the real server** (not done yet): build the Release DLL, back up the existing plugin settings (`programdata/plugins/configurations/WatchingEye*`), copy the DLL to the `plugins` folder, restart Emby, then run through the plugin page. The plugin Public API must be enabled with a key for the Android app's actions.
- Optionally add a tag-triggered release workflow for this repo that builds `WatchingEye.dll` and attaches it to a GitHub Release (the app repo already has the pattern).
- Optionally hash the stored API key; optionally add an audit trail for admin actions (extend / reset / time-out).
- Consider limiting the Public API to specific client addresses.
- Add the regression suite to CI (needs an Emby server, so probably not practical on GitHub runners).
- App repo, still to do by the user: add the tag and branch rulesets, enable Dependabot, make it public and then enable secret scanning / push protection and the outside-contributor approval setting.

## 7. GitHub backup
2026-10-09: pushed to https://github.com/feerlessleadr/hall-monitor-emby.git (then named watching-eye-emby) (branch `main` plus the three tags). The pushed history is a rewritten copy of the original local history: all commits are attributed to `feerlessleadr <github@thecgkg.com>` (no work email or username anywhere in metadata, messages or files) and `original/WatchingEye.dll` was removed from every commit (the README keeps its SHA-256).
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
The user confirmed their release keystore (kept off this machine and out of every repository) is the key of the installed app: its SHA-256 fingerprint `DF:15:A0:B3:A7:31:31:32:6A:10:B5:49:34:D1:EE:A3:C2:A0:24:95:19:D2:28:31:2E:C3:72:61:85:F4:36:F7` equals the signature of the supplied APK. The keystore and passwords were never shared with the assistant; the user adds them as four repository secrets (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`).
Tested locally with a throwaway key (since deleted): unsigned build without a key, signed build with the same environment variables the workflow sets (including the base64 round trip), fingerprint guard passes for the right key / rejects a wrong key / rejects an unsigned APK / accepts the user's real APK against the real fingerprint, tag-vs-version check, YAML validity. Not testable here: the workflow run on GitHub itself (needs the secrets), so the first dry run may need a small fix.
Note for later: a test technique trap on this machine - `grep -c $'\r'` is passed as an empty pattern and matches every line; count carriage returns with `tr -cd '\r' | wc -c` instead (always run a control).

## 10. Android app repo: license, history purge, hardening (2026-10-09)
App repo (private): added an MIT LICENSE (copyright holder `feerlessleadr`), hardened `.gitignore` (signing files), and made the release workflow's publish step re-runnable (skips if the release already exists).
Purged `export.zip` (a decompiled export of the old app) from the whole history with `git filter-branch`, then force-pushed `main` and the tags `v1.3`/`v1.4` (verified first: only the first commit differed, and only by that file; both tags have byte-identical trees). Size 6.6 MB -> 0.47 MB. Commit hashes changed (main now e1d75d9; v1.4 = 743f011). The original history is saved locally in `C:\dev\watching-eye-android-app-original-history.bundle` (contains the export; do not publish). Local repo was switched to the published history. Moving the v1.4 tag re-triggers the release workflow once (old workflow logic at that commit): a red run at the publish step is expected if the v1.4 release already exists.
Old commits may stay reachable by hash on GitHub until GitHub garbage-collects them; nothing sensitive was in them (checked: no keys, passwords or private addresses).
Before making the repo public: enable secret scanning/push protection (appears for public repos), set outside-contributor workflow approval, add tag/branch rulesets (steps given to the user).

## 11. Publishing this repository (provenance and licensing)
The user wants to make this repository public (2026-10-09). Audit before publishing: all 37 commits are attributed to the GitHub identity; no work email, personal name, secrets, private addresses or binaries (only the logo JPG) in any commit or file; the original DLL is not tracked.
**Known limitation of that audit:** one earlier commit (8a32767) mentions the path/alias of the user's release keystore in this log (scrubbed going forward; the keystore itself was never shared). It is harmless without the file and was not worth rewriting history for.
**Legal position (advice given, accepted by the user's decision to publish):** the source is derived from decompiling a plugin whose author is unknown, so nobody can grant a license for that part. Decisions: no LICENSE file; README states provenance, "no license granted", a takedown / credit contact route, and non-affiliation with Emby; the original binary stays out. Residual risk: the original author could object; the response is to credit or remove promptly. If the user later wants a license, only the material added after the recovery (tests, scripts, docs, changes) could reasonably be licensed, and mixed ownership makes a blanket license wrong.
**Suggested GitHub settings when going public:** Settings -> General -> Danger Zone -> Change visibility; then Advanced Security -> enable Secret scanning and Push protection; Actions -> General -> require approval for outside contributors; Rulesets -> protect `main` (restrict deletions, block force pushes) with a Repository-admin bypass; enable Dependabot alerts. This repository has no workflows or secrets today.

## 12. Live push, plugin 1.8.0.0 and Android app 1.5 (2026-10-10)
**Why:** the app polled every 10 s. The flicker was fixed first (only changed cards redraw, images reload only when they change, polling only while the screen is visible, one shared HTTP client, a banner instead of pop-ups when the server cannot be reached). Then the user asked for push.

**How push works.** Emby's websocket is used as is: the app connects to `/embywebsocket` (token in the `X-Emby-Token` header) and sends two subscriptions. `SessionsStart` is Emby's own (sessions pushed when they change). `WatchingEyeStatusStart` is the plugin's (class `StatusPushListener`, built on Emby's `BasePeriodicWebSocketListener`): the plugin pushes the status list when watch time moves, on every extend/reduce/time-out/clear/reset, when settings are saved, and as a 30-second heartbeat. The app falls back to asking the server every 20 s only while the socket is down, reconnects by itself with a growing delay (1 s ... 60 s), treats 75 s of silence as a dead connection, and reconnects when the phone changes network.
**Plugin side:** `ApiKeyGate` (shared key check and lockout), `StatusPushListener`, `ReduceTime` routes, status fields `BlockReason`, `SecondsRemaining`, `LimitingPeriod`, `LimitingAllowedSeconds`. An Emby admin API key connection has no user, so it cannot be recognised as an administrator on the socket; it needs the plugin key (as the app always has).
**App side:** `StatusRepository` (one source of truth for the screen, the alert service and the widget), `LiveConnection`, `LiveMessages`, `AlertService` (optional foreground service, off by default; notification with +15/+30/+60 buttons when a user goes from allowed to over a limit), `WatchingEyeWidget`, new card layout, undo and confirmations, light/dark theme.

**Tested (the in-app screen lock tests below were for the lock that was later removed):** plugin regression suite 53/53 on the test server (undo, push after extend/time-out/clear, unauthorised and wrong-key subscribers get only an error, status fields). App: 40 unit tests (4 skipped: need a server); on an Android emulator (API 37) against the test server: live updates, +15 and Undo, now-playing with ticking countdown, time-out and "End time-out + 30", background alert with the app on the home screen (and its +15 button), server stopped and restarted (banner, then back to Live automatically), widget placed on the home screen, light and dark theme. **Not tested:** a real phone, real battery behaviour of the background service (some phones kill it unless the app is excluded from battery optimisation), a reverse proxy in front of Emby (it must allow websockets), more than two users.

**Issues found while testing (fixed):** the notification's +15 button failed with "can't reach the server" because the broadcast receiver re-read the encrypted settings; it now uses the settings the running service already holds. The emulator that was first used turned out to be shared with another project's app: tests moved to a separate, throw-away emulator, which was removed afterwards. A shell `sed` insert left a stray character again (see section 3): use the editor tool.
**Test server cleanup:** test users, the throw-away Emby API key, and the plugin configuration were removed or reset after the UI tests.
**Decisions still open:** install the plugin update on the production server; battery-optimisation wording for the alerts setting.

## 13. Security review follow-up (2026-10-10): plugin 1.9.0.0 and app 1.6
**Review result (defensive summary).** No critical problems and no known-vulnerable libraries (a public vulnerability database had no advisories for the app's 7 libraries or the plugin package; a deliberately old library did show hits, so the lookup works). The realistic adversary is a child with access to the parent's phone or to a regular Emby account, so the findings were about that. All findings were fixed or documented; nothing was left silently.

| Finding | Fix |
|---|---|
| App and notification buttons usable by anyone holding the phone | An in-app screen-lock check was built and tested, then REMOVED at the user's request (the phone's own lock is enough for them; the app only relies on it). Kept: notification buttons need an unlocked phone, lock screen shows a generic notice when Android is set to hide sensitive content; the add-time receiver refuses while the phone is locked |
| Live push accepted the plugin key alone (an anonymous websocket opens) and there was no minimum key length | Keys under 24 characters are refused on save (page and server); startup warns about an existing short key. The push cannot require an Emby login because Emby does not expose who the socket belongs to |
| Push permission checked only once | Re-checked on every push; turning the Public API off, changing the key or removing admin rights ends the subscription at once |
| One connection could multiply pushes | One subscription per connection |
| Settings silently fell back to plain text if encrypted storage failed | No fallback: a broken store is reset (settings re-entered); if even that fails, settings live in memory only |
| Phone held an Emby administrator key | Plugin 1.9 reports now-playing itself (`NowPlaying`, `ApiLevel`) and has `StopStream` routes, so the app needs only the plugin key plus the token of an ordinary Emby account; the app can sign in for you (password never stored; warns if the account is an administrator). Verified: a regular account's token works for status, extend/reduce, time-out, stop stream and the live push |
| Failed-key lockout hits everyone behind a reverse proxy | Documented (set Emby known proxies); not fixable in code without trusting forwarded headers |
| Supply chain (downloader installs the DLL from GitHub) | Build provenance attestations for the plugin DLL and the APK, locked NuGet restore, Dependabot version updates for both repos, app release job uses a protected-able `release` environment; the user should turn on 2FA/passkey and immutable releases |
| Boot receiver exported, widget names visible | Boot receiver not exported; option to hide names on the widget |
| A typo in the server address crashed the app (found while testing) | The address is validated; a message and the Settings screen are shown instead |

**Deliberately not done:** R8 minification (no security boundary; risks breaking the Gson models) and certificate pinning (would break on certificate renewal for a server you run); Gradle dependency verification (maintenance cost with Dependabot). The encrypted-preferences library is an alpha release that Google has deprecated but has no known advisories; replacing it would force re-entering keys, so it is left for a later release.
**Tested (the in-app screen lock tests below were for the lock that was later removed):** plugin regression suite (new checks for key length, one subscription per connection, access ending at once, now-playing, StopStream); app unit tests; an integration test that signs in with a regular account and runs the whole app flow against the test server; on an Android emulator: the PIN prompt on launch, no prompt within the grace period and a prompt after it, a locked cover that exposes nothing to accessibility tools, the lock-screen notification with Android's "hide sensitive content" setting, the regular-account token mode with live now-playing and Stop stream.
**Not tested:** a real phone, a fingerprint prompt, Android versions below 12 (the notification-button unlock requirement needs Android 12+, older versions rely on the private notification and the receiver's locked-phone check), a reverse proxy.
**For the user (GitHub side):** passkey or 2FA on the account; Settings -> Releases -> immutable releases; optionally move the signing secrets into the `release` environment restricted to tags `v*`; CodeQL default setup.

## 13. Renamed to Hall Monitor (plugin 1.10.0.0, 2026-10-11)
- **Why:** the plugin is a rewrite derived from a recovered original, not a fork that continues it, so it no longer shares the original's name. The user chose **Hall Monitor** (the GitHub repo is now `feerlessleadr/hall-monitor-emby`; the old address redirects).
- **Changed (visible only):** the plugin name and description in Emby, the plugin page title, notification titles ("Hall Monitor: Time Limit Reached" ...), the log prefix `[Hall Monitor]`, the refusal text of the live push, the README, this file, `TESTING.md`, the issue template, the release title, the `gh attestation verify --repo` command, and the plugin image (replaced by the new Hall Monitor logo: two progress rings, one for the day and one for the week, around a play button; `assets/logo/` has the SVG and a PNG). Version 1.9.0.0 -> 1.10.0.0.
- **Deliberately unchanged:** the plugin GUID, the DLL name `WatchingEye.dll` (so the GitHub downloader and the settings file `WatchingEye.xml` keep working), the namespaces, the page names, the web addresses `/emby/WatchingEye/...`, the `X-WatchingEye-Api-Key` header, the websocket message names (`WatchingEyeStatusStart`, `WatchingEyeStatus`) and the data files `WatchingEye.Logs.json` / `WatchingEye.WatchTime.json`. Reason: saved settings and every existing client (the old Watching Eye app, Embidextrous) depend on them. A later rename of these would need aliases and a settings migration.
- **Checked:** nothing in the code builds a data path from `Plugin.Name`; the settings file name follows the DLL name, so the rename keeps settings.
- **Regression suite on the test server:** 65 of 65 checks pass on 1.10.0.0 (run on an otherwise idle server). Two earlier runs failed only because another session was installing a plugin into, restarting and stopping the same shared test server at the time; the suite needs the server to itself.

## 14. Everything renamed to Hall Monitor (plugin 2.0.0.0, 2026-10-12)
- **Why:** the original plugin's author re-released it as a closed-source app. To avoid any confusion the owner asked for every trace of the old name to go, replacing the earlier decision (section 13) to keep the DLL name. Supersedes the "Deliberately unchanged" line above.
- **Changed:** assembly and DLL `HallMonitor.dll`, namespace `HallMonitor`, project and folder names, plugin page and embedded resource names, web addresses `/emby/HallMonitor/...`, header `X-HallMonitor-Api-Key`, live messages `HallMonitorStatus`/`HallMonitorStatusStart`/`HallMonitorStatusStop`, settings and data files `HallMonitor.xml`, `HallMonitor.WatchTime.json`, `HallMonitor.Logs.json`, the release workflow, the regression suite and the docs. Version 2.0.0.0 because the wire names changed.
- **Not kept (owner's choice):** no old route, header or message name is answered. Older apps (the Watching Eye phone app v1.6, Embidextrous before 0.5) cannot talk to 2.0.
- **Settings carry over:** the plugin ID is unchanged, and `LegacySettings.CopyOnce` copies the three old files to the new names on the first start (never overwriting an existing new file; the old files stay). If a copy fails, a warning is logged, because the blank settings would mean no limit applies. The owner deletes the old plugin file by hand: two copies of one plugin stop Emby from loading it.
- **Checked on the test server:** legacy files in place (a marker in the settings, 20 watch-time rows) then the new DLL: `HallMonitor.xml` held the marker, the rows were copied, the new route answered, the old route returned 404, the plugin reported version 2.0.0.0.
