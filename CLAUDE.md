# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

.NET 11 / C# 15 bindings + wrapper for [miniaudio](https://github.com/mackron/miniaudio), aimed at the Ahjo game engine. Structure and conventions follow the sibling repo [Ahjo-Vulkan](https://github.com/pekkah/Ahjo-Vulkan); when in doubt about a pattern, look there first.

## Load-bearing invariants

1. **Generated code is generated.** Never hand-edit `src/Ahjo.Miniaudio.Native/Generated/`. Edit `tools/generate-miniaudio.rsp` (or `MiniaudioDefines`) and regenerate (`/regen-bindings`). Hand-written additions to the Native project go in a sibling `Manual/` folder.
2. **One define list, two consumers.** `MiniaudioDefines` in `Directory.Build.props` is passed to both cmake and ClangSharp. Never add a struct-shaping `MA_*` define to only `CMakeLists.txt` or only the rsp — the bindings would silently disagree with the binary about `sizeof`.
3. **`LayoutTests` is the oracle.** It compares every listed generated struct against the C compiler's `sizeof` (and the fields the wrapper touches against `offsetof`) via the `ahjo_ma_sizeof_*` / `ahjo_ma_offsetof_*` exports in `native/miniaudio/src/ahjo_miniaudio.c`, on every RID lane. A failure there is a real layout bug: never change the expected value to make it pass. When the wrapper starts allocating a new miniaudio type, add it to both lists: an `AHJO_MA_SIZEOF(T)` line in `ahjo_miniaudio.c` and an entry in `LayoutTests.Types`; when it starts reading or writing a field of a generated struct that miniaudio itself fills in or reads back (not a `ma_*_config` it only writes before an init call), the same for `AHJO_MA_OFFSETOF(T, F)` and `LayoutTests.Fields`. Config field offsets are covered by `tools/check-bindings-portable.sh` plus the per-RID `sizeof` check, not by `Fields`.
4. **Native AOT stays clean** — `IsAotCompatible=true` on `src/` projects; no reflection discovery or dynamic codegen reachable from the wrapper. CI publishes `samples/HelloAudio` with `PublishAot=true` and runs it on the null backend.
5. **Zero per-frame allocations** on anything the audio thread or a game frame calls (data callbacks, sound playback control, listener/spatial updates). Setup-time allocation is fine.
6. **`TreatWarningsAsErrors=true`** with `AnalysisLevel=latest`. Fix the diagnostic; don't `#pragma` it away.
7. **The sample tracks the public API.** When the wrapper gains a feature, `samples/HelloAudio` moves from the raw `Ma.*` calls to it — the sample is the first consumer and shows the intended usage.
8. **Opaque types are opaque.** The types in `src/Ahjo.Miniaudio.Native/Manual/Opaque.cs` are empty C# structs, so `sizeof` of one is 1 and `NativeBlock.Alloc<T>()` of one corrupts memory. Allocate `Ma.ahjo_ma_sizeof_<T>()` bytes, and read a field through a miniaudio getter or a new `ahjo_ma_*` accessor in `ahjo_miniaudio.c` (imported in `Manual/Ma.Ahjo.cs`) — never by declaring the field in C#.

## Platform layouts (one set of bindings, every RID)

miniaudio's runtime-state structs are platform-specific: `ma_context` / `ma_device` carry per-backend members under `MA_SUPPORT_WASAPI` / `MA_SUPPORT_ALSA` / …, and `ma_mutex` / `ma_event` / `ma_semaphore` / `ma_thread` are `HANDLE`s on Windows but `pthread_*` types on POSIX. The structs that embed those by value move with them: `ma_resource_manager`, `ma_log`, `ma_fence`, `ma_async_notification_event`, `ma_job_queue`, `ma_device_job_thread`. `ma_engine`, `ma_sound`, `ma_decoder`, `ma_node_graph` and every `ma_*_config` do **not** — they hold the runtime state by pointer and are laid out the same on every target (as of 0.11.25).

So the platform-dependent types are **opaque** (invariant 8): the rsp excludes them and `Manual/Opaque.cs` declares them empty; the binary reports their size. Also excluded or remapped, because they have no portable C# shape: the `*_w` entry points and `ma_log_postv` (`wchar_t` and `va_list` differ per target), and `wchar_t` itself, which becomes `void` in the remaining pointer fields. The bindings are generated for one pinned target, `x86_64-pc-windows-msvc`, and that one managed assembly serves every RID. `tools/check-bindings-portable.sh` proves it at regen time: parsing for linux-x64, linux-arm64 or osx-arm64 differs only in enum base type and `char` signedness, neither of which changes a layout. The parse-only POSIX shims in `native/stubs/` (`pthread.h`, `stdalign.h`) exist for that check; their sizes are dummies because nothing generated embeds them. Don't swap them for `MA_NO_PTHREAD_IN_HEADER`, which changes layouts and so could only go through `MiniaudioDefines` (invariant 2).

A new RID lands as: a lane in `build-miniaudio-native.yml` and `ci.yml` with passing `LayoutTests` on that RID → then the RID in `PackMiniaudioRuntimes`. Linux builds on the oldest hosted Ubuntu (22.04, so glibc 2.35 is the floor); the macOS dylib targets 12.0 (`CMakeLists.txt`).

## Project shape

```
src/
  Ahjo.Miniaudio.Native/   ClangSharp P/Invokes against miniaudio.h (class `Ma`, library `ahjo_miniaudio`) + the host-RID native build;
                           Manual/ holds the opaque runtime-state types and the ahjo_ma_* imports
  Ahjo.Miniaudio/          idiomatic wrapper for Ahjo (not packed yet): Devices/ (context, device,
                           render callback), Decoding/ (decoder, PcmBuffer), Engine/ (engine, sounds,
                           groups, pools, listener); design in docs/design/specs/*issue-02*
native/
  miniaudio/include/       miniaudio.h at the pinned tag — committed, the generator input of record
  miniaudio/src/           ahjo_miniaudio.c: the single TU (implementation + sizeof oracle)
  miniaudio/CMakeLists.txt shared library; MA_DLL exports, static MSVC CRT
  stubs/                   parse-time libc (+ POSIX pthread) shims so codegen needs no system toolchain
samples/
  HelloAudio/              usage tour (tone + file playback); also the Native AOT publish check in CI
tests/
  Ahjo.Miniaudio.Native.Tests/   version pin, layout oracle, null-backend context + device
  Ahjo.Miniaudio.Tests/          wrapper tests
tools/generate-miniaudio.rsp     ClangSharp configuration
tools/check-bindings-portable.sh regen-time check that the bindings fit every target
docs/design/                     specs/ (what + why) and plans/ (how), named YYYY-MM-DD-issue-NN-<topic>
```

Gitignored, never edit: `native/miniaudio/downloaded/`, `native/miniaudio/build/`, `native/miniaudio/staged/`.

## Common commands

```bash
dotnet tool restore
dotnet build Ahjo.Miniaudio.slnx          # also builds ahjo_miniaudio for the host via cmake
dotnet test                               # Microsoft.Testing.Platform (see global.json)
dotnet build src/Ahjo.Miniaudio.Native -t:Regenerate   # refetch pinned header + regenerate bindings

# one project / one test (xunit v3 on MTP: --filter-class, --filter-method, wildcards ok)
dotnet test --project tests/Ahjo.Miniaudio.Native.Tests --filter-method "*VersionMatchesPinnedRelease"

# sample: tone by default, a file path to stream it, --null for no speakers
dotnet run --project samples/HelloAudio -- --null
dotnet publish samples/HelloAudio -c Release -r win-x64   # the Native AOT check CI runs (per RID)
```

The native build needs cmake and a C toolchain (MSVC on Windows). It is incremental on the header, the TU, `CMakeLists.txt` and `Directory.Build.props`.

## Tests

Every test runs on miniaudio's **null backend**, which simulates a device on a timer thread. Hosted CI runners have no audio hardware; the null backend is what lets the full context/device lifecycle (including the data callback on miniaudio's own thread) run there honestly. Tests that need real audio output are local-only and must say so in their skip reason.

## Version pin

`MiniaudioVersion` in `Directory.Build.props` pins a **release tag** (`master` only moves on releases; `dev` is the unreleased next patch, `dev-0.12` the next major). `VersionMatchesPinnedRelease` asserts the loaded binary reports exactly the pin.

## Release

Versions come from MinVer (`v*` tags). `publish.yml` packs a preview on every push to `main` and a stable package on a GitHub Release. It pushes to NuGet.org only when the `NUGET_PUBLISH` repo variable is `true` **and** `NUGET_KEY` is set; otherwise packages are workflow artifacts only. The shipped native binary comes from `build-miniaudio-native.yml`, which both CI and publish call, and which runs `Ahjo.Miniaudio.Native.Tests` before uploading.

## Commit + PR style

Commits: `<area>: <imperative>` — e.g. `Native: bump miniaudio to 0.11.26`, `CI: add linux-x64 lane`. PRs reference their issue (`Closes #NN`) and merge to `main`.
