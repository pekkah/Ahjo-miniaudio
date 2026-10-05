# Ahjo.Miniaudio

Idiomatic, low-allocation .NET wrapper for [miniaudio](https://github.com/mackron/miniaudio),
built for the Ahjo game engine. It sits on top of `Ahjo.Miniaudio.Native`, which
carries the raw bindings and the native binary.

> **Status:** in development. The API surface is not yet stable.

Three tiers that stack:

| Tier | Types | For |
|---|---|---|
| Device | `AudioContext`, `AudioDevice`, `IAudioRenderer` | an audio thread you render into ("buffers out") |
| Decoder | `AudioDecoder`, `PcmBuffer` | WAV / FLAC / MP3 bytes → interleaved `f32` PCM in native memory |
| Engine | `AudioEngine`, `SoundAsset`, `Sound`, `SoundGroup`, `SoundPool`, `AudioListener` | game playback: mixing buses, 3D spatialization |

## Playing sounds

```csharp
using Ahjo.Miniaudio;

using var engine = AudioEngine.Create();          // default device, started
using var sfx    = SoundGroup.Create(engine);     // a bus

// Setup: load once, create voices.
using var shot  = SoundAsset.Decode(File.ReadAllBytes("shot.wav"), engine.SampleRate);
using var shots = SoundPool.Create(engine, shot, voices: 8,
    new SoundDescription { Group = sfx, Spatialized = true });

using var music = SoundAsset.Stream(File.ReadAllBytes("theme.flac"));
using var track = Sound.Create(engine, music, new SoundDescription { Looping = true });
track.Play();

// Per frame: none of this allocates.
engine.Listener.SetPose(cameraWorldMatrix);       // System.Numerics, −Z forward
shots.Play(muzzlePosition);
sfx.Volume = settings.EffectsVolume;
```

Create sounds at setup and play them per frame. A `Sound` is a voice: hold one
per emitter, or a `SoundPool` per overlapping effect. Never create one per shot.

## Device changes and failures

An engine that opens its own device reports on it two ways:

```csharp
using var engine = AudioEngine.Create(new AudioEngineDescription { DeviceObserver = watcher });

// Pushed, on a miniaudio thread: Rerouted (the default output changed),
// Stopped (if you didn't call Stop, the device was lost).
sealed class Watcher : IAudioDeviceObserver
{
    public void OnNotification(AudioDeviceNotification n) { /* record it for the game thread */ }
}

// Polled, from the game thread:
if (!engine.IsStarted && !pausedByGame) { /* device lost: Start() or recreate */ }
```

An `AudioDevice` reports the same notifications through its renderer, which is
also an `IAudioDeviceObserver`. In both cases an exception thrown by the observer
is kept in `Fault` instead of crashing the audio thread.

## Rendering your own audio

```csharp
sealed class Synth : IAudioRenderer
{
    public void Render(Span<float> output, int channels) { /* audio thread: no allocation, no locks */ }
}

using var device = AudioDevice.Create(context: null, default, new Synth());
device.Start();
```

An engine created with `NoDevice = true` mixes only when `engine.Read(span)` is
called, so it can be one input to such a renderer, next to other audio.

## Lifetime

Every type is `IDisposable`. A parent disposes whatever is still alive on it:
a context disposes its devices and engines, and an engine disposes its sounds
and groups. Disposing in the wrong order is therefore safe. A `SoundAsset` is
reference-counted by its sounds, so disposing it while they play is fine.

This also holds across threads. Disposing a `NoDevice` engine while a device is
still pulling it waits for the read in progress. After that, the device reports
`ObjectDisposedException` in its `Fault` and plays silence. Dispose the device
first to avoid that fault.

## Errors

Native failures throw `MiniaudioException` with the raw `ma_result`. Misuse
throws the standard `Argument*`, `ObjectDisposed` and `InvalidOperation`
exceptions.

## Backends that never answer

Opening a context, device or engine talks to the system's audio server, and
some backends wait for it with no timeout: PulseAudio blocks forever on a
server that accepted the connection and then stopped responding (seen with
WSLg). `AudioContext.Create`, `AudioDevice.Create` and `AudioEngine.Create`
take a `CancellationToken` so you can give up:

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
AudioContext context;
try
{
    context = AudioContext.Create(default, timeout.Token);
}
catch (OperationCanceledException)
{
    context = AudioContext.Create(new AudioContextDescription { Backend = AudioBackend.Null });
}
```

miniaudio cannot interrupt that wait, so cancelling stops *you* waiting. It
doesn't stop the init. The init keeps running on a background thread, and its
memory is released whenever it returns. If it never returns, one parked thread
and that allocation stay until the process exits. Without a token, `Create`
blocks just as miniaudio does.

## Platform support

Follows `Ahjo.Miniaudio.Native`: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`
and `osx-arm64`.

## Licensing

MIT. miniaudio itself is Public Domain / MIT No Attribution; its license text
ships in `Ahjo.Miniaudio.Native`.
