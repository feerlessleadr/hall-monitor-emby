# Testing Hall Monitor

All tests run against a **test** Emby server. Nothing here is meant for a production server: the scripts create a
throw-away user, change the plugin configuration, and restore the plugin to a clean disabled state when they finish.

## One-time setup
1. Install the Release DLL in the test server's `plugins` folder and restart Emby:
   `dotnet build src/HallMonitor -c Release` -> copy `src/HallMonitor/bin/Release/net6.0/HallMonitor.dll`.
2. Create an Emby **admin API key** (Dashboard -> API Keys) and save it, alone, in a text file.
   The scripts default to `%USERPROFILE%\.emby-spike-key`; use `-KeyFile` for another path. Never commit this file.
3. The test library needs at least one item with id `15` (the scripts report playback of that item). Any media item works;
   edit `-ItemId` in `tools/Common.ps1` (`Send-PlaybackReport`) if your test server uses different ids.
4. PowerShell scripts are blocked by default on Windows: run them as shown below (the bypass is process-only).

## Run the regression suite (about 3 minutes)
```
powershell -ExecutionPolicy Bypass -File tools\Run-RegressionTests.ps1 [-BaseUrl http://localhost:8096] [-KeyFile <path>] [-SkipEnforcement] [-SkipLockout]
```
It prints `[PASS]` / `[FAIL]` per check and exits with code 1 if anything failed. Last result: **34 of 34 passed**.

| Area | What is checked |
|------|-----------------|
| Setup | plugin loaded; config contains none of the removed features |
| Authorization | non-admin refused (403) on admin routes; anonymous refused (401); non-admin cannot read the plugin config |
| Public API | disabled -> refused; enabled -> works; wrong / missing key -> 403; no Emby login -> 401 |
| Input validation | minutes out of range, unknown / oversized user ids, unknown period; credit never overflows; time-out with different-case id; time-out refused for unlisted user |
| Undo (ReduceTime) | takes back part of an extension; never below zero; bad minutes / unknown user rejected; non-admin refused; public route needs the key |
| Live push | subscribing sends the status at once; extend / time-out / clear are pushed immediately; versions only grow; a normal user without the key, and a wrong key, get only an error (uses `tools/ws-push-client.ps1`) |
| Status contract | every field the app reads, including `BlockReason`, `SecondsRemaining`, `LimitingPeriod`, `LimitingAllowedSeconds` |
| Configuration | out-of-range values are clamped; an enabled limit of 0 is refused (400) and not saved |
| Enforcement | time counted; Stop + message at the limit; LimitReached event; new playback blocked while over limit; time-out message states the local end time |
| Name sanitising | markup in a client name never reaches the event log |
| Lockout | after repeated wrong keys even the correct key is refused (lasts 5 minutes or until Emby restarts; skip with `-SkipLockout`) |

If the lockout check ran, the Public API stays locked for the test client for 5 minutes: wait, restart Emby, or use `-SkipLockout` on the next run.

## Helpers
- `tools/Common.ps1` - shared functions (API calls, throw-away user, config save, state reset). Dot-source it.
- `tools/ws-listen.ps1` - acts as an Emby client device over the websocket and logs every message the plugin sends (Stop, DisplayMessage). Pass `-Token`, `-DeviceId`, `-Out`.

## Manual checks (browser)
Open the plugin page (Dashboard -> Plugins -> Hall Monitor) in a **fresh page load** (Emby Web caches plugin pages):
tabs are Watch Time Limiter / Logging / External API; Add User, edit a user, an enabled limit of 0 shows an error;
Generate New Key makes a 64-character hex key and Clear Key empties it; Save shows "Settings saved."

## Known harness pitfalls
- Use descriptive session ids (not single letters) and `-LiteralPath` when deleting files in scripts.
- PowerShell variable names are case-insensitive (`$s` and `$S` are the same variable).
- A real player is not used: the scripts report playback through Emby's session API, so the Stop command is observed on the
  websocket, not in a player. Emby Web in an embedded browser pane may ignore Stop.
- Emby's own server log keeps request URLs (including session tokens) - use throw-away users only.

## The test server is shared
Other sessions on this machine (for example the GitHub plugin work) use the same Emby test server. Installing a plugin, restarting or stopping the server while the suite runs makes it fail in confusing ways (503 "Emby Server is loading", a stray extra push, missing users). Run it when nothing else is using the server, and delete the suite's leftover `we-*` / `O'Brien & Co` users and run `Reset-PluginState` after an aborted run.
