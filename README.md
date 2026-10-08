# Watching Eye (recovered source)

Emby Server plugin that monitors and limits per-user watch time. (Transcode warnings/blocking from the original plugin were removed in this fork.)

> **Recovered via decompilation.** The original author deleted the GitHub repository. This source was
> reconstructed from `WatchingEye.dll` **v1.6.8.0** (`original/WatchingEye.dll`, SHA-256
> `099F8C1D474ECACA05384701DC4D3FFA7DB76379C7B83EEA748C8E2547CCDEBD`) using `ilspycmd` 11.1.
> Original author is unknown (AssemblyCompany is empty; copyright line "© 2025").
> Comments, original formatting and any non-shipped files (tests, docs) are lost.

- Plugin name: `Watching Eye` · GUID: `e8c3b1b3-4f56-4f38-a28a-2e6c5a043007` (must not change, Emby identifies the plugin by it)
- Target: `net6.0`, references `MediaBrowser.Server.Core` 4.9.1.90 (NuGet)

## Build

    dotnet build src/WatchingEye -c Release

Output: `src/WatchingEye/bin/Release/net6.0/WatchingEye.dll`. Copy that single DLL to Emby's `plugins` folder and restart Emby.

See `PROGRESS.md` for the recovery log, findings and the feature summary.
