# Ahjo.Miniaudio — Claude Project Memory

.NET 11 / C# 15 bindings + wrapper for [miniaudio](https://github.com/mackron/miniaudio), aimed at the Ahjo game engine. Structure and conventions follow the sibling repo [Ahjo-Vulkan](https://github.com/pekkah/Ahjo-Vulkan); when in doubt about a pattern, look there first.

## Load-bearing invariants

1. **Generated code is generated.** Never hand-edit `src/Ahjo.Miniaudio.Native/Generated/`. Edit `tools/generate-miniaudio.rsp` (or `MiniaudioDefines`) and regenerate (`/regen-bindings`). Hand-written additions to the Native project go in a sibling `Manual/` folder.
2. **One define list, two consumers.** `MiniaudioDefines` in `Directory.Build.props` is passed to both cmake and ClangSharp. Never add a struct-shaping `MA_*` define to only `CMakeLists.txt` or only the rsp — the bindings would silently disagree with the binary about `sizeof`.
3. **`LayoutTests` is the oracle.** It compares every listed generated struct against the C compiler's `sizeof` via the `ahjo_ma_sizeof_*` exports in `native/miniaudio/src/ahjo_miniaudio.c`. A failure there is a real layout bug: never change the expected value to make it pass. When the wrapper starts allocating a new miniaudio type, add it to both lists.
4. **Native AOT stays clean** — `IsAotCompatible=true` on `src/` projects; no reflection discovery or dynamic codegen reachable from the wrapper. CI publishes `samples/HelloAudio` with `PublishAot=true` and runs it on the null backend.
5. **Zero per-frame allocations** on anything the audio thread or a game frame calls (data callbacks, sound playback control, listener/spatial updates). Setup-time allocation is fine.
6. **`TreatWarningsAsErrors=true`** with `AnalysisLevel=latest`. Fix the diagnostic; don't `#pragma` it away.
7. **The sample tracks the public API.** When the wrapper gains a feature, `samples/HelloAudio` moves from the raw `Ma.*` calls to it — the sample is the first consumer and shows the intended usage.

## Platform layouts (why only win-x64 ships)

miniaudio's runtime-state structs are platform-specific: `ma_context` / `ma_device` carry per-backend members under `MA_SUPPORT_WASAPI` / `MA_SUPPORT_ALSA` / …, and `ma_mutex` / `ma_event` / `ma_semaphore` / `ma_thread` are `HANDLE`s on Windows but `pthread_*` structs on POSIX. Everything that embeds them (`ma_engine`, `ma_resource_manager`, `ma_sound`, …) inherits the difference.

The bindings are therefore generated for one target, `x86_64-pc-windows-msvc` (pinned in the rsp), and only `win-x64` is built, tested and packed. Adding another RID needs a decision first — the two candidates:

- **Opaque state + native sizeof.** Exclude the platform-dependent structs from generation, declare them as empty opaque structs in `Manual/`, and allocate them from the `ahjo_ma_sizeof_*` exports. Config structs (`ma_*_config`) are platform-independent apart from pointer size and stay generated. One managed assembly for all RIDs.
- **Per-RID bindings.** Generate per target triple and ship RID-specific managed assemblies. More faithful, much heavier to build and pack.

Whichever is chosen, a new RID lands as: CI lane with passing `LayoutTests` on that RID → then the RID in `PackMiniaudioRuntimes`.

## Project shape

```
src/
  Ahjo.Miniaudio.Native/   ClangSharp P/Invokes against miniaudio.h (class `Ma`, library `ahjo_miniaudio`) + the host-RID native build
  Ahjo.Miniaudio/          idiomatic wrapper for Ahjo (not packed yet)
native/
  miniaudio/include/       miniaudio.h at the pinned tag — committed, the generator input of record
  miniaudio/src/           ahjo_miniaudio.c: the single TU (implementation + sizeof oracle)
  miniaudio/CMakeLists.txt shared library; MA_DLL exports, static MSVC CRT
  stubs/                   parse-time libc shims so codegen needs no system toolchain
samples/
  HelloAudio/              usage tour (tone + file playback); also the Native AOT publish check in CI
tests/
  Ahjo.Miniaudio.Native.Tests/   version pin, layout oracle, null-backend context + device
  Ahjo.Miniaudio.Tests/          wrapper tests
tools/generate-miniaudio.rsp     ClangSharp configuration
```

Gitignored, never edit: `native/miniaudio/downloaded/`, `native/miniaudio/build/`, `native/miniaudio/staged/`.

## Common commands

```bash
dotnet tool restore
dotnet build Ahjo.Miniaudio.slnx          # also builds ahjo_miniaudio for the host via cmake
dotnet test                               # Microsoft.Testing.Platform (see global.json)
dotnet build src/Ahjo.Miniaudio.Native -t:Regenerate   # refetch pinned header + regenerate bindings
```

The native build needs cmake and a C toolchain (MSVC on Windows). It is incremental on the header, the TU, `CMakeLists.txt` and `Directory.Build.props`.

## Tests

Every test runs on miniaudio's **null backend**, which simulates a device on a timer thread. Hosted CI runners have no audio hardware; the null backend is what lets the full context/device lifecycle (including the data callback on miniaudio's own thread) run there honestly. Tests that need real audio output are local-only and must say so in their skip reason.

## Version pin

`MiniaudioVersion` in `Directory.Build.props` pins a **release tag** (`master` only moves on releases; `dev` is the unreleased next patch, `dev-0.12` the next major). `VersionMatchesPinnedRelease` asserts the loaded binary reports exactly the pin.

## Commit + PR style

Commits: `<area>: <imperative>` — e.g. `Native: bump miniaudio to 0.11.26`, `CI: add linux-x64 lane`. PRs reference their issue (`Closes #NN`) and merge to `main`.
