<p align="center"><img src="assets/logo/hall-monitor-512.png" alt="Hall Monitor: two progress rings around a play button, one for the day and one for the week" width="128"></p>

# Hall Monitor (for Emby)

Emby Server plugin that limits how long selected users can watch: per-user daily / weekly / monthly / yearly caps,
per-day limits, allowed time windows, extra time, and time-outs. Only users listed under *Limited Users* are affected.

> **Version 2.0 renames everything to Hall Monitor.** The file is now `HallMonitor.dll`, the web addresses are
> `/emby/HallMonitor/...`, the key header is `X-HallMonitor-Api-Key`, the live-update messages are `HallMonitorStatus`,
> and the settings files are `HallMonitor*` in `programdata/plugins/configurations`. The earlier names are no longer
> answered, so older apps that used them stop working; use the Embidextrous app 0.5 or newer. The plugin GUID is
> unchanged, and the first start of 2.0 copies your old settings, watch-time data and log into the new files once.
> **To update:** delete the previous `.dll` from Emby's `plugins` folder (two copies of one plugin stop Emby from
> loading it), put `HallMonitor.dll` there and restart Emby. The old settings files are left in place and can be
> deleted once you are happy.

> **Derived from a recovered plugin.** The original author deleted the GitHub repository. This source was reconstructed from
> its compiled file **v1.6.8.0** (the original binary is not included in this repository; SHA-256
> `099F8C1D474ECACA05384701DC4D3FFA7DB76379C7B83EEA748C8E2547CCDEBD`) using `ilspycmd` 11.1, then rewritten and maintained
> by hand. The original author is unknown (AssemblyCompany is empty; copyright line "(c) 2025"). Comments and any original
> tests/docs were lost. Features unrelated to watch time (transcode warnings, library restrictions, global rules,
> exclusions, ...) were removed; see `PROGRESS.md` for every decision.

- Plugin name: `Hall Monitor` - GUID: `e8c3b1b3-4f56-4f38-a28a-2e6c5a043007` (must not change; Emby identifies the plugin by it)
- Target: `net6.0`, references `MediaBrowser.Server.Core` 4.9.1.90 (NuGet). Tested on Emby Server 4.10.1.0.

## Build and install
    dotnet build src/HallMonitor -c Release

Copy `src/HallMonitor/bin/Release/net6.0/HallMonitor.dll` to the Emby server's `plugins` folder and restart Emby.
Back up the existing plugin settings (`programdata/plugins/configurations/HallMonitor*`) first.

## Verify a download
Releases are built by GitHub Actions and come with signed build provenance. To check that a downloaded `HallMonitor.dll`
really was built from this repository (needs GitHub's `gh` tool):

    gh attestation verify HallMonitor.dll --repo feerlessleadr/hall-monitor-emby

The `.sha256` file next to the DLL only detects a damaged download; the attestation is what proves where it came from.
Build with the exact packages recorded in `src/HallMonitor/packages.lock.json`: `dotnet restore src/HallMonitor --locked-mode`.

## Using the Android app safely
The companion app is [Embidextrous](https://github.com/feerlessleadr/embidextrous) (its Hall Monitor tab). The notes below apply to it.
- The app needs the plugin key (Public API tab) and an Emby **token**. Prefer the token of an ordinary, non-administrator
  Emby user (the app can sign in for you; only the token is kept) over an administrator API key: with plugin 1.9 or newer the
  app needs nothing else, so a lost phone does not hand over your server.
- The plugin key must be at least 24 characters (the page's **Generate New Key** makes 64). Short keys are refused on save.
- Behind a reverse proxy, tell Emby the proxy's address (Settings -> Network -> known proxies) and allow websockets,
  otherwise all clients look like the proxy: a few wrong keys can lock out the app, and the live updates cannot connect.

## Documentation
- `PROGRESS.md` - current state, decision log, issues and workarounds, security review summary, backlog
- `TESTING.md` - how to run the regression suite (`tools/Run-RegressionTests.ps1`) against a test server

## Provenance, licensing and takedown
- **This is a derivative work.** The plugin source was recovered by decompiling a compiled plugin whose author could not be
  identified (the assembly has no company or author information, and its original repository was deleted). The original
  author keeps whatever rights they have in that work. This repository does **not** include the original binary.
- **No open-source license is granted for the recovered code.** No license file is provided on purpose: the original author's
  terms are unknown, so nobody can license their part. Without a license, the default is that all rights are reserved;
  GitHub's terms still let you view and fork this repository. The added material (tests, scripts, documentation and the
  changes made after the recovery) is offered for reading and reference only unless a license is added later.
- **If you are the original author** (or hold rights in the original plugin) and want this repository credited, changed
  or removed, please open an issue or contact the repository owner. It will be handled promptly.
- This is an independent, unofficial project and is not affiliated with or endorsed by Emby / Premiere Tech.
- The software is provided as is, without warranty. Test it on a non-production server first.
