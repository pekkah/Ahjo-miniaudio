# `Ahjo.Miniaudio.Native`: cross-platform support through opaque runtime state

**Issue:** [#6](https://github.com/pekkah/Ahjo-miniaudio/issues/6) — *Native: cross-platform support via opaque runtime state (linux-x64, linux-arm64, osx-arm64)*
**Date:** 2026-10-04

## Problem

The bindings were generated for `x86_64-pc-windows-msvc`, so only `win-x64` was
shipped. Some miniaudio structs are laid out differently on each platform. On a
POSIX RID, a binding generated from the Windows header has the wrong `sizeof` for
those structs, and the bug shows up as heap corruption. CLAUDE.md listed two ways
out: opaque state with a native sizeof, or per-RID bindings. The issue picks
opaque state. This document records what that choice turned into once it was
built.

## Evidence

**E1. What moves.** We generated bindings for `x86_64-pc-linux-gnu`,
`aarch64-linux-gnu` and `arm64-apple-macos` and diffed each against the Windows
output. These types change layout:

- the runtime state: `ma_context`, `ma_device`, `ma_resource_manager`, `ma_log`,
  `ma_fence`, `ma_async_notification_event`, `ma_job_queue`, `ma_device_job_thread`;
- the sync typedefs: `ma_mutex`, `ma_event`, `ma_semaphore`, `ma_thread`;
- two WASAPI-only types: `ma_IMMNotificationClient`, `ma_context_command__wasapi`.

`ma_engine`, `ma_sound`, `ma_decoder`, `ma_node_graph` and the `ma_*_config`
structs do not move. They hold the runtime state by pointer. CLAUDE.md said they
moved too; that was wrong for 0.11.25 and has been fixed.

**E2. Differences that don't move a layout.** These were left as they are:

- An enum's base type is `int` on MSVC and `uint` everywhere else.
- `char` is `sbyte` on Windows but `byte` on linux-arm64.

**E3. Differences that would break a signature.** Excluding the moving structs
was not enough to make the outputs match:

| Construct | Windows | POSIX | Resolution |
| --- | --- | --- | --- |
| `*_w` entry points (14) | `ushort*` | `int*` | excluded; callers use the UTF-8 variants |
| `pFilePathW`, `onOpenW`, `onInitFileW` | `ushort*` | `int*` | `--remap wchar_t=void` → `void*` |
| `ma_device_id.wasapi` (`ma_wchar_win32`) | typedef of `wchar_t` | `ma_uint16` | `--remap ma_wchar_win32=ushort` (keeps it 16-bit after the `wchar_t` remap) |
| `ma_log_postv(…, va_list)` | `sbyte*` | `__va_list_tag*` | excluded; C# cannot build a `va_list` portably |
| `ma_mutex_*` / `ma_event_*` / `ma_semaphore_*` | `void**` (HANDLE erased) | struct pointer | excluded from generation, remapped to themselves → `ma_mutex*` etc. |

With all of these applied, the four targets produce identical output apart from
the E2 differences. `tools/check-bindings-portable.sh` checks this.

**E4. Every place the wrapper touches runtime state.**

- `AudioContext`: `->backend`.
- `AudioDevice`: `->playback.channels`, `->sampleRate`, and `->pUserData` in
  `OnData` and `OnNotification`.
- `AudioEngine`: `->pUserData` on the notification's device.

`ma_device.playback.channels` comes after the device's mutex and event members,
so its offset differs on POSIX.

## Decisions

1. **One managed assembly, generated for one pinned target.** The rsp keeps
   `--target=x86_64-pc-windows-msvc`. It excludes and remaps the E1 and E3 items.
   The excluded structs are declared empty in `Manual/Opaque.cs`.
2. **The binary supplies sizes.** Each opaque type has an `ahjo_ma_sizeof_*`
   export, imported in `Manual/Ma.Ahjo.cs`. The wrapper allocates
   `ma_context` and `ma_device` with these sizes.
3. **Fields are read through accessors, not declared.** There are four
   accessors: `ahjo_ma_context_get_backend`, `ahjo_ma_device_get_user_data`,
   `ahjo_ma_device_get_playback_channels` and `ahjo_ma_device_get_sample_rate`.
   Each one is a single load with no locking and no callbacks.
   `ahjo_ma_device_get_user_data` is `[SuppressGCTransition]` because `OnData`
   calls it once per period. `AudioDevice` caches the channel count at init, so
   that call is the only one `OnData` makes.
4. **The oracle checks offsets as well as sizes.** `AHJO_MA_OFFSETOF` covers each
   field of a portable struct that the wrapper reads or writes directly:
   `ma_engine.pProcessUserData`, `ma_device_notification.{pDevice,type}`,
   `ma_device_info.{id,name,isDefault}` and `ma_audio_buffer_ref.sampleRate`.
   The opaque types are no longer part of the sizeof comparison. Two checks
   cover them instead. First, each opaque type has an import that returns the
   native size. Second, no generated struct may embed an opaque type by value.
   The sizeof oracle can't catch that case unless the outer struct is in its
   list.
5. **POSIX parsing uses shims, not `MA_NO_PTHREAD_IN_HEADER`.**
   `native/stubs/pthread.h` declares dummy-sized types. Nothing generated embeds
   them, so their size has no effect. `stdalign.h` is empty, `stddef.h` gains
   `wchar_t` for non-MSVC targets, and `stdarg.h` now uses
   `__builtin_va_list`. The define would have changed layouts (invariant 2).
6. **Each RID must pass its own lane before it ships.** win-x64, win-arm64,
   linux-x64, linux-arm64 and osx-arm64 each run the native lane (build,
   `Ahjo.Miniaudio.Native.Tests`, artifact). Each also runs the solution lane
   (all tests, then a Native AOT publish of HelloAudio and a run on the null
   backend). Linux builds on ubuntu-22.04, which makes glibc 2.35 the minimum.
   The dylib sets a macOS 12.0 deployment target.

## Public API changes in `Ahjo.Miniaudio.Native`

- `ma_context`, `ma_device`, `ma_resource_manager`, `ma_log`, `ma_fence`,
  `ma_async_notification_event`, `ma_job_queue` and `ma_device_job_thread` are
  opaque. They have no fields, and `sizeof` of any of them is meaningless.
- `ma_mutex`, `ma_event`, `ma_semaphore` and `ma_thread` are now opaque structs.
  Their functions used to take `void**`.
- `pFilePathW`, `onOpenW` and `onInitFileW` are typed `void*` instead of
  `ushort*`.
- These are removed: the 14 `*_w` functions, `ma_log_postv`,
  `ma_IMMNotificationClient` and `ma_context_command__wasapi`.
- New: the `ahjo_ma_sizeof_*` imports for the opaque types, and the four
  accessors.

## Rejected

- **Per-RID bindings.** Only two of the structs the wrapper allocates move, and
  E4 lists every place it touches them. Per-RID bindings would need several
  generated trees and RID-specific assemblies, all to keep the fields of
  structs the wrapper only reaches through a pointer.
- **A CI gate on the cross-target diff.** The E2 differences make the raw diff
  noisy. The `LayoutTests` run on every lane is the CI gate. The normalized
  diff runs at regen time instead (regen-bindings skill, step 4).
