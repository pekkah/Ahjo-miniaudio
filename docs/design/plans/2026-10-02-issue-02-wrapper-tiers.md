Paired with [../specs/2026-10-02-issue-02-wrapper-tiers-design.md](../specs/2026-10-02-issue-02-wrapper-tiers-design.md)

# Plan — issue #2: device, decoder and engine tiers

All new files are in `src/Ahjo.Miniaudio/`, namespace `Ahjo.Miniaudio`, `unsafe` where
they touch `Ma.*`. Each folder corresponds to one tier.

## 1. Shared plumbing (`Internal/`, root)

- `MiniaudioException.cs`: `public sealed class MiniaudioException : Exception` with
  `ma_result Result` and `string Operation`. The message is
  `"{operation} failed: {ma_result_description} ({result})"`.
- `Internal/MaCheck.cs`: `static void ThrowIfFailed(ma_result, string operation)`.
- `Internal/NativeBlock.cs`: `static T* Alloc<T>()` (64-byte aligned, zeroed) and
  `static void Free(void*)`.
- `Internal/Units.cs`: `ToVector3(ma_vec3f)` and `ToFrames(TimeSpan, uint sampleRate)`.
- Public enums. Each value equals its `ma_*` counterpart so that conversion is a cast:
  `AudioBackend` (all 15 `ma_backend` members), `AudioDeviceNotification` (the 6 `ma_device_notification_type`
  members), `AttenuationModel` (None, Inverse, Linear, Exponential) and `Positioning`
  (Absolute, Relative).

## 2. Device tier (`Devices/`)

- `AudioContextDescription` (`AudioBackend? Backend`) and `AudioContext`: `Create`,
  `Backend`, `GetPlaybackDevices()` (copies `ma_context_get_devices` output into
  `AudioDeviceInfo[]`), and `Dispose` (`ma_context_uninit` + free). An `internal ma_context* Native`
  is exposed for the engine and device.
- `AudioDeviceId`: a readonly struct wrapping `ma_device_id`, `IEquatable` via
  `ma_device_id_equal`. `AudioDeviceInfo` is a readonly record struct
  (`string Name`, `AudioDeviceId Id`, `bool IsDefault`).
- `AudioDeviceDescription` (`AudioDeviceId? PlaybackDevice`, `int Channels`,
  `int SampleRate`, `int PeriodSizeInFrames`, all 0 = native) and `IAudioRenderer`, as in the
  spec.
- `AudioDevice`: `Create(AudioContext?, in AudioDeviceDescription, IAudioRenderer)`,
  `Start`, `Stop`, `IsStarted`, `Channels`, `SampleRate`, `Exception? Fault`,
  and `Dispose`. The callbacks are `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]`
  statics reached through `GCHandle<AudioDevice>` in `pUserData`. The render callback
  silences the output and latches the fault through `Interlocked.CompareExchange`, as the
  spec describes. `Dispose` calls `ma_device_uninit` and only then frees the handle.

## 3. Decoder tier (`Decoding/`)

- `PcmBuffer`: `Allocate(ulong frameCount, int channels, int sampleRate)`, `Samples`,
  `FrameCount`, `Channels`, `SampleRate`, `internal float* Pointer`, `Dispose`. It rejects
  more than `int.MaxValue` samples.
- `AudioDecoderDescription` (`int Channels`, `int SampleRate`, 0 = native).
- `AudioDecoder`: `Create` (copies the bytes and runs `ma_decoder_init_memory` with an f32
  output config), `Read`, `Seek`, `LengthInFrames` (`ma_decoder_get_length_in_pcm_frames`),
  `Channels`, `SampleRate`, `Dispose`, and `static PcmBuffer DecodeAll(...)`. `DecodeAll`
  reads to the end in chunks. It sizes the buffer from the length when the length is known,
  and grows it by doubling otherwise.
- `internal static` helper: `InitDecoder(byte* data, nuint size, in description, ma_decoder*)`,
  shared with streamed sounds.

## 4. Engine tier (`Engine/`)

- `AudioEngineDescription` and `AudioEngine`: `Create` (maps the description, applies the
  `NoDevice` 2 ch / 48 kHz default and validates `ListenerCount` 0..4), `Start`, `Stop`,
  `Volume`, `Channels`, `SampleRate`, `TimeInFrames`, `Read`, `Listener`, `GetListener` and
  `Dispose`. It keeps a child registry, a `LinkedList<IEngineChild>` under a `Lock`. Each
  child stores its own node.
- `AudioListener`: a readonly struct (engine + index) with the members the spec lists.
  `SetPose` takes the translation as the position, the negated third row (normalized) as
  the direction, and the second row (normalized) as the world-up.
- `SoundAsset`: `Decode`, `Stream` and `FromPcm`, plus `Channels`, `SampleRate`,
  `LengthInFrames` and `IsStreamed`. It is reference-counted through `AddRef` and
  `Release` (Interlocked), and the native memory is freed at zero.
- `SoundGroup`: `Create(engine, parent?)`, `Volume`, `Pan`, `Pitch`, `Start`, `Stop`,
  `Dispose`. It holds an `internal ma_sound_group* Native`.
- `SoundDescription` (`SoundGroup? Group`, `bool Looping`, `bool Spatialized`).
- `Sound`: the native block is `ma_sound` plus either an `ma_audio_buffer_ref` (decoded;
  `sampleRate` patched before `ma_sound_init_from_data_source`) or an `ma_decoder`
  (streamed). It carries the member list from the spec. `Play` runs
  `ma_sound_reset_stop_time_and_fade`, seeks to 0 and starts. `Dispose` uninitializes the
  sound and then its data source, releases the asset and unregisters from the engine.
- `SoundPool`: `Sound[]` voices plus a round-robin cursor. `Play()` scans for a voice
  that is not playing and otherwise takes the oldest one by cursor. It returns the
  `Sound`.

## 5. Layout oracle

Add the types the wrapper newly allocates or reads by pointer to both lists:
`ma_audio_buffer_ref`, `ma_device_id`, `ma_device_notification` and `ma_vec3f`
(returned by value from the position getters) (in
`native/miniaudio/src/ahjo_miniaudio.c` and in `LayoutTests.Types`).

## 6. Tests (`tests/Ahjo.Miniaudio.Tests/`, all on the null backend or `NoDevice`)

- `TestAudio.cs`: an in-memory 16-bit PCM WAV writer (sine or constant), so no fixture
  files are needed.
- Device: the null context reports `Null` and at least one playback device. A device
  renderer is called with the device's channel count. A throwing renderer latches `Fault`,
  and the device keeps running. The `Stopped` notification arrives on `Stop`.
- Decoder: a WAV round-trip covering channels, rate, length and approximate samples;
  `Seek`; `DecodeAll`; resampling 44100 → 48000 scales the length; garbage bytes throw
  `MiniaudioException`.
- Engine (deterministic, `NoDevice` + `Read`): a constant-PCM sound produces non-zero
  output; volume 0 produces silence after smoothing; `AtEnd` arrives after about
  length × rate ratio frames (E5); `Play` after `StopWithFade` is audible again (E6);
  `Looping` never reaches the end; a group's volume 0 silences its sounds; a streamed asset
  plays.
- Lifetime: engine `Dispose` with live sounds and groups is safe, and the children report
  disposed. Disposing an asset with a live sound keeps the sound playing. Double `Dispose`
  is harmless.
- Pool: N voices play concurrently, and N+1 steals.
- Listener: `SetPose` maps the matrix rows as described.
- Allocations: `GC.GetAllocatedBytesForCurrentThread` is 0 across a loop of setters,
  `Play`, `SoundPool.Play`, listener updates and `engine.Read`.

## 7. Sample and docs

- `samples/HelloAudio`: the tone is generated into a `PcmBuffer` → `SoundAsset.FromPcm` →
  a looping `Sound` with a pan/pitch sweep. A file argument uses
  `SoundAsset.Stream(File.ReadAllBytes(path))`. The new `--pull` flag builds a `NoDevice`
  engine and plays it through an `AudioDevice` whose renderer calls `engine.Read`. This
  exercises the unmanaged callback under AOT.
- CI: add a run of `HelloAudio.exe --null --pull` next to the existing `--null` run.
- `src/Ahjo.Miniaudio/README.md`: a usage section for each tier.

## Left open

- Packing `Ahjo.Miniaudio` (`IsPackable`, the `publish.yml` step) is a release decision
  for the maintainer.
