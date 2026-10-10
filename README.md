# Watching Eye (recovered fork)

Emby Server plugin that limits how long selected users can watch: per-user daily / weekly / monthly / yearly caps,
per-day limits, allowed time windows, extra time, and time-outs. Only users listed under *Limited Users* are affected.

> **Recovered via decompilation.** The original author deleted the GitHub repository. This source was reconstructed from
> `WatchingEye.dll` **v1.6.8.0** (the original binary is not included in this repository; SHA-256
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
