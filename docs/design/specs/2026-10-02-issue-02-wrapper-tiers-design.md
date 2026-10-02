# `Ahjo.Miniaudio` — the wrapper: device, decoder and engine tiers

**Issue:** [#2](https://github.com/pekkah/Ahjo-miniaudio/issues/2) — *Wrapper: device, decoder and engine tiers for Ahjo*
**Consumer:** the Ahjo engine (`c:/p/Ahjo`), which has no audio module yet
**Date:** 2026-10-02

## Problem

`Ahjo.Miniaudio` has one public member, `MiniaudioLibrary.Version`
(`src/Ahjo.Miniaudio/MiniaudioLibrary.cs:14`). Everything else a consumer does goes
through the raw `Ma.*` bindings, and `samples/HelloAudio/Program.cs` shows the cost:

- every miniaudio object is hand-allocated in aligned native memory because it must
  not move while initialized (`Program.cs:37`, `:45`, `:86-87`, `:139`, helper at `:195-200`);
- every `ma_result` is checked by a private helper and a private exception type
  (`Program.cs:202-210`) that no consumer can catch by type;
- init/uninit pairs nest four `try`/`finally` deep (`Program.cs:84-133`) to get the
  reverse-order teardown right.

The consumer's needs are more specific than "wrap miniaudio". The Ahjo spec has its own
audio plan:

- `Ahjo.Audio — graph-based audio (MetaSounds analogue)` (`docs/ahjo-engine-spec.md:95`),
  "node-graph DSP … executing on the audio thread from a baked graph; spatialization …,
  attenuation/occlusion fed by physics queries" (`:425`). It is milestone M3 (`:571`),
  Lane A of the implementation plan (`docs/ahjo-implementation-plan.md:588-597`).
- "**Audio thread**: real-time priority, lock-free command queue in, buffers out"
  (`docs/adr/0002-threading-model.md:27`).
- "audio … buffers live in `NativeMemory`" (`docs/ahjo-engine-spec.md:55`).
- BlockBreaker records "Audio — no module; spec M3, plan Lane A"
  (`docs/adr/0026-blockbreaker-sample.md:196`). Games have no sound until M3.

The wrapper therefore has to serve two horizons at once. In the near term, games need
sound effects and music. In the long term, Ahjo's own graph needs an audio thread to
render into, and asset import needs to decode audio.

## Evidence

### E1. The three tiers compose; they do not compete

`ma_engine_config.noDevice` (`miniaudio.h:11305`) builds an engine with no device, and
`ma_engine_read_pcm_frames` (`:11346`) pulls mixed frames from it on demand. The header's
own example calls it from inside a caller-owned device's data callback (`:1070-1087`).
An engine is therefore one possible input to a device callback, not an alternative to
one. When `noDevice` is set, `ma_engine_init` requires explicit `channels` and
`sampleRate` and fails with `MA_INVALID_ARGS` otherwise (`:77625-77626`).

### E2. Ahjo loads assets as bytes, not paths

Ahjo has no VFS. Loaders read whole files and decode from spans, for example
`ImageAssetLoader` and `KtxNativeImage.TryOpen(ReadOnlySpan<byte>, …)` in `src/Ahjo.Assets/`.
`ma_decoder_init_memory` (`miniaudio.h:10038`) fits that model. It does **not** copy:
the bytes must outlive the decoder.

### E3. The resource manager's streaming path is file/VFS-only

`ma_resource_manager_register_encoded_data` (`:10551`) is "Does not copy", and the name it
registers serves only *data buffers*. Data streams always open their decoder through
`ma_decoder_init_vfs` on a path (`:70250`, `:73215`). Streaming music from memory through
the resource manager would therefore need a custom `ma_vfs` that maps names to bytes, plus
a background job thread and a string-keyed global table.

### E4. Cloning a sound only works for resource-manager buffers

`ma_sound_init_copy` returns `MA_INVALID_OPERATION` unless the source sound came from the
resource manager (`:78615-78618`). It also `ma_malloc`s a data source per copy
(`:78624`). "Load once, play many" therefore needs its own instancing story.

### E5. `ma_audio_buffer_ref` reports sample rate 0

`ma_audio_buffer_ref_init` takes no sample rate and hard-codes
`sampleRate = 0  /* TODO: Version 0.12 */` (`:60065`). The field is public
(`:5905`). A sound built on it with no fix-up plays at the engine's rate whatever the
data's rate, so a 44.1 kHz effect on a 48 kHz engine comes out about 9% sharp.

### E6. Stop-with-fade is sticky

`ma_sound_stop_with_fade_*` "will overwrite any scheduled stop and fade. If you want to
restart the sound, first reset it with `ma_sound_reset_stop_time_and_fade()`" (`:11402`).
A naive `Start()` after a fade-out stays silent.

### E7. Device notifications are the only signal of a lost or rerouted device

`ma_device_config.notificationCallback` (`:7115`) delivers `started`, `stopped`,
`rerouted`, `interruption_began`, `interruption_ended` and `unlocked` (`:6778-6783`).
They arrive on miniaudio's threads, including from inside `ma_device_uninit`
(the stop notification, `:20220`).

### E8. The consumer's conventions

- All math uses `System.Numerics`, with row-vector matrices. A camera's forward is the
  **negated third row** and its up is the second row (`src/Ahjo.App/ActiveCamera.cs:59`).
  Local forward is −Z, which is miniaudio's default listener direction.
- Errors: the low-level wrappers throw. `Ahjo.Rhi` catches `VulkanException`, reads its
  raw result and returns a typed `RhiResult<T>`.
- Hot paths carry no allocations (`[HotPath]` analyzers). `GCHandle` *pinning* is banned.
  Non-pinning handles are not mentioned.
- Packages are consumed as pinned NuGet packages (`Ahjo.Vulkan*` 0.13.0 in
  `Directory.Packages.props`).

## Decision

One assembly, three tiers, all in namespace `Ahjo.Miniaudio`. Every miniaudio object a
tier allocates lives in 64-byte-aligned `NativeMemory` owned by a `sealed class … :
IDisposable`. Only the `ma_*` *config* structs live on the stack, and only for the
duration of the init call.

### Errors

`MiniaudioException(ma_result result, string operation)` is public. It exposes `Result`
(the raw `ma_result`) and `Operation`, and its message adds `ma_result_description`. It
mirrors `VulkanException`, so an `Ahjo.Audio` layer can convert it to a typed result the
way `Ahjo.Rhi` does. Misuse raises the standard BCL exceptions instead:
`ArgumentException`/`ArgumentOutOfRangeException` for bad descriptions,
`ObjectDisposedException`, and `InvalidOperationException` (for example `Read` on an
engine that owns its device).

Descriptions are `readonly record struct`s that are **valid by default**: every zero means
"miniaudio's default" or "the device's native value". Where miniaudio has no default,
the wrapper fills one in. A `NoDevice` engine with 0 channels or 0 Hz gets 2 channels at
48 kHz instead of `MA_INVALID_ARGS` (E1).

### Tier 1 — device

```csharp
AudioContext.Create(in AudioContextDescription)   // Backend: AudioBackend? (null = platform order)
  .Backend, .GetPlaybackDevices() -> AudioDeviceInfo[]   // Name, Id, IsDefault
AudioDevice.Create(AudioContext?, in AudioDeviceDescription, IAudioRenderer)
  .Start(), .Stop(), .IsStarted, .Channels, .SampleRate, .Fault

public interface IAudioRenderer
{
    void Render(Span<float> output, int channels);                 // audio thread
    void OnNotification(AudioDeviceNotification notification) { }  // miniaudio thread
}
```

- The output format is always interleaved `f32` for playback. Capture and duplex are out
  of scope until a consumer needs them.
- The callbacks are `[UnmanagedCallersOnly]` statics. The device's `pUserData` is a
  **normal, non-pinning** `GCHandle<AudioDevice>`: the device object itself is never
  pinned, which respects Ahjo's ban on pinning (E8). The handle is freed only *after*
  `ma_device_uninit` returns, because uninit still delivers a notification (E7).
- If a renderer throws, the exception is caught inside the callback. That callback writes
  silence, and the exception is latched into `Fault`: the first fault wins. Once faulted,
  later callbacks write silence without calling the renderer again, so a broken renderer
  produces one exception rather than one per period. An escaped exception on a foreign
  thread would kill the process.
- Steady-state callbacks allocate nothing: a handle lookup, an interface call and a
  `Span` over the native buffer.

This is the "buffers out" thread for Ahjo's Lane A graph. Its lock-free command queue is
Ahjo's own and belongs on the consumer's side of the `Render` call.

### Tier 2 — decoder

```csharp
PcmBuffer.Allocate(frameCount, channels, sampleRate)   // NativeMemory, interleaved f32
  .Samples (Span<float>), .FrameCount, .Channels, .SampleRate
AudioDecoder.Create(ReadOnlySpan<byte> encoded, in AudioDecoderDescription)  // Channels/SampleRate 0 = native
  .Read(Span<float>) -> frames, .Seek(frame), .LengthInFrames, .Channels, .SampleRate
AudioDecoder.DecodeAll(ReadOnlySpan<byte> encoded, in AudioDecoderDescription) -> PcmBuffer
```

`AudioDecoder.Create` **copies** the encoded bytes into native memory it owns. One copy at
load time is how a `ReadOnlySpan<byte>` API stays safe against the non-copying
`ma_decoder_init_memory` (E2). The caller's array may then be collected.

### Tier 3 — engine

```csharp
AudioEngine.Create(in AudioEngineDescription)
  // Context?, PlaybackDevice?, NoDevice, Channels, SampleRate, PeriodSizeInFrames,
  // ListenerCount (0 → 1, max 4), NoAutoStart
  .Start(), .Stop(), .Volume, .Channels, .SampleRate, .TimeInFrames
  .Read(Span<float>)                       // NoDevice only; callable from a device callback
  .Listener, .GetListener(int) -> AudioListener (readonly struct: engine + index)

AudioListener: Position, Direction, WorldUp, Velocity, Enabled, SetCone(...), SetPose(in Matrix4x4)

SoundAsset.Decode(ReadOnlySpan<byte>, sampleRate = 0)  // decoded once to PCM; instances share it
SoundAsset.Stream(ReadOnlySpan<byte>)                  // bytes copied once; each Sound decodes on the fly
SoundAsset.FromPcm(PcmBuffer)                          // takes ownership; e.g. cooked or generated PCM

SoundGroup.Create(AudioEngine, SoundGroup? parent = null)   // a mixing bus: Volume, Pan, Pitch, Start/Stop
Sound.Create(AudioEngine, SoundAsset, in SoundDescription)  // Group?, Looping, Spatialized
  .Play()                // restart from 0: reset stop/fade (E6), seek, start
  .Start() / .Stop()     // resume / pause at cursor
  .StopWithFade(TimeSpan), .Fade(from, to, TimeSpan), .Seek(frame)
  .IsPlaying, .AtEnd, .CursorInFrames, .LengthInFrames
  .Volume, .Pan, .Pitch, .Looping, .Spatialized, .Position, .Direction, .Velocity,
  .Positioning, .AttenuationModel, .Rolloff, .MinDistance, .MaxDistance, .DopplerFactor,
  .SetCone(...)
SoundPool.Create(AudioEngine, SoundAsset, int voices, in SoundDescription)
  .Play() / .Play(Vector3 position) -> Sound   // idle voice, else steals the least-recently played
```

**The resource manager is not used** (E3, E4). A decoded asset is a `PcmBuffer`, and each
`Sound` over it gets its own `ma_audio_buffer_ref`: a cursor over shared, borrowed PCM,
with the sample rate patched before the sound is initialized (E5). A streamed asset is its
copied bytes, and each `Sound` gets its own `ma_decoder` over them, which `ma_sound` reads
as a data source. The engine still builds its default resource manager internally. It is
unused but harmless. Removing it needs `MA_NO_RESOURCE_MANAGER`, a struct-shaping define
(CLAUDE.md invariant 2), and is left for later.

**Instancing is the voice, not the asset.** Creating a `Sound` is setup-time work: one
managed object, one native block and one data-source init. Gameplay must not create a
`Sound` per gunshot. It holds a `Sound` per emitter, or a `SoundPool` per effect, and
calls `Play()`. Every member a frame calls (`Play`, `Stop`, setters, `IsPlaying`,
listener updates, `SoundPool.Play`, `AudioEngine.Read`) allocates nothing. A test
measures this with `GC.GetAllocatedBytesForCurrentThread`.

**`Spatialized` defaults to `false`.** A default `SoundDescription` gives a 2D sound,
which suits UI and music and is valid by default. 3D emitters opt in. The spatializer
then follows `Position` against the listener, and the listener follows the camera
through `SetPose`.

### Lifetime

Native teardown order is miniaudio's hardest rule. The wrapper enforces it so that a
wrong disposal order cannot cause a use-after-free:

- **Context → devices and engines.** Each device and engine opened on an
  `AudioContext` registers with it. `AudioContext.Dispose` disposes the live ones
  first and then calls `ma_context_uninit`.
- **Engine → children.** The engine registers every `Sound` and `SoundGroup` created on
  it, at setup time under a lock. `AudioEngine.Dispose` disposes the live ones in reverse
  creation order, so sounds go before the groups they were created into. It then calls
  `ma_engine_uninit`. Disposing a child unregisters it in O(1).
- **Asset → sounds.** `SoundAsset` is reference-counted. Each `Sound` holds a reference.
  `SoundAsset.Dispose` releases the owner's reference, and the native PCM or bytes are
  freed when the count reaches zero. Disposing an asset that is still playing is legal,
  and the audio keeps playing.
- **Group → sounds.** When `ma_sound_group_uninit` runs on a group with sounds attached,
  `ma_node_uninit` detaches them. They go silent but are not corrupted.
- `Dispose` never throws and is idempotent. Every other member throws
  `ObjectDisposedException` after disposal.

**The engine clock only runs while something is attached.** `ma_engine_get_time_in_pcm_frames`
returns the endpoint node's local time. That time advances by the frames the endpoint
actually reads, and an endpoint with no attached nodes reads none. A sound or group that
exists on the engine counts as attached even when it is stopped. `TimeInFrames` documents
this, and `EngineTests.ReadAdvancesTheClockAndRejectsPartialFrames` pins it down.

### Threading

miniaudio's sound and listener setters are atomic against the audio thread. The wrapper
adds no locks on those paths. Each object should be driven from one thread at a time,
normally the game thread. `AudioEngine.Read` runs on whichever single thread pulls it,
normally a device's audio thread. Creation, disposal and the engine's child registry take
a lock and are setup-time only.

## How Ahjo consumes it

This is a sketch for `Ahjo.Audio`, not part of this change:

1. `AhjoSimulationFeature.Audio` creates one `AudioEngine`, the sound groups as buses
   (Music, SFX, UI) and the assets through the asset store's bytes (`SoundAsset.Decode` /
   `.Stream`).
2. A `Presentation`-phase system calls `ActiveCamera.TryFind` to get the camera pose and
   then `engine.Listener.SetPose(pose)`. An emitter component holds a `Sound` or a
   `SoundPool` and copies `LocalToWorld` translation into `Position`.
3. At M3, Lane A's executor becomes an `IAudioRenderer` on an `AudioDevice`. If games keep
   their SFX, a `NoDevice` engine is pulled inside that renderer with `engine.Read` and
   mixed in. Decoding for the graph's sample players uses `AudioDecoder`.

## Why not the alternatives

- **Engine tier only.** This leaves Lane A with no audio thread to build on, so the
  device tier would have to be retrofitted later beneath an API already in use.
- **Device and decoder only.** Games would get no sound until M3, roughly 18 months
  away (`ahjo-engine-spec.md:571`).
- **Resource manager plus a custom VFS.** This brings a string-keyed global table, a job
  thread and async loading semantics. Ahjo's loader is synchronous over bytes (E2), so it
  buys nothing here. Its streaming also needs the VFS shim (E3).
- **`ma_sound_init_copy` for instancing.** It only works on resource-manager buffers and
  calls `malloc` per copy (E4).
- **`ma_engine_play_sound` for one-shots.** It is fire-and-forget with an internal
  `malloc` and offers no handle to move or stop the sound. `SoundPool` covers the same
  need without allocating.
- **A delegate for the render callback.** A delegate field captures less than an
  interface but forces a `GetFunctionPointerForDelegate`-style bridge or a closure. The
  interface plus a non-pinning handle is AOT-clean and has one dispatch.
- **Throwing from `Dispose` when children are alive.** CA1065, and it turns an ordering
  mistake into a crash in a `finally`. Owning the order is cheaper.
- **Sound end callbacks.** They would add a third unmanaged callback running on the audio
  thread. Pools and emitters poll `IsPlaying`/`AtEnd` from the game thread, which is
  where they would have to marshal the event anyway. Deferred until a consumer asks.

## Out of scope

Capture and duplex devices, custom data sources and nodes (`ma_node` effects), the
resource manager and VFS, end callbacks, encoding, `MA_NO_RESOURCE_MANAGER`, and packing
`Ahjo.Miniaudio` (`IsPackable` stays `false`; flipping it is a release decision).
