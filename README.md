# Watching Eye (recovered fork)

Emby Server plugin that limits how long selected users can watch: per-user daily / weekly / monthly / yearly caps,
per-day limits, allowed time windows, extra time, and time-outs. Only users listed under *Limited Users* are affected.

> **Recovered via decompilation.** The original author deleted the GitHub repository. This source was reconstructed from
> `WatchingEye.dll` **v1.6.8.0** (`original/WatchingEye.dll`, SHA-256
> `099F8C1D474ECACA05384701DC4D3FFA7DB76379C7B83EEA748C8E2547CCDEBD`) using `ilspycmd` 11.1, then maintained by hand.
> The original author is unknown (AssemblyCompany is empty; copyright line "(c) 2025"). Comments and any original
> tests/docs were lost. Features unrelated to watch time (transcode warnings, library restrictions, global rules,
> exclusions, ...) were removed in this fork; see `PROGRESS.md` for every decision.

- Plugin name: `Watching Eye` - GUID: `e8c3b1b3-4f56-4f38-a28a-2e6c5a043007` (must not change; Emby identifies the plugin by it)
- Target: `net6.0`, references `MediaBrowser.Server.Core` 4.9.1.90 (NuGet). Tested on Emby Server 4.10.1.0.

## Build and install
    dotnet build src/WatchingEye -c Release

Copy `src/WatchingEye/bin/Release/net6.0/WatchingEye.dll` to the Emby server's `plugins` folder and restart Emby.
Back up the existing plugin settings (`programdata/plugins/configurations/WatchingEye*`) first.

## Documentation
- `PROGRESS.md` - current state, decision log, issues and workarounds, security review summary, backlog
- `TESTING.md` - how to run the regression suite (`tools/Run-RegressionTests.ps1`) against a test server
