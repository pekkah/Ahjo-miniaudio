// HelloAudio — the smallest useful tour of Ahjo.Miniaudio.
//
//   HelloAudio                 play a 440 Hz tone that pans left → right
//   HelloAudio <file>          stream a WAV / FLAC / MP3 file to the end
//   HelloAudio ... --null      use miniaudio's null backend (no speakers
//                              needed; the device runs on a timer — CI uses this)
//   HelloAudio ... --pull      the engine opens no device of its own; an
//                              AudioDevice pulls its mix from a render callback
//                              (how a custom DSP graph would host it)
//
// The pattern to take away:
//
//   * setup creates things: a context, an engine, assets, sounds. Each is
//     IDisposable, and a parent disposes whatever is still alive on it.
//   * per frame, a game only plays sounds and moves them (Play, Pan, Pitch,
//     Position, the listener). None of that allocates.

using Ahjo.Miniaudio;

namespace HelloAudio;

internal static class Program
{
    private static int Main(string[] args)
    {
        var useNullBackend = args.Contains("--null");
        var pull = args.Contains("--pull");
        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        Console.WriteLine($"miniaudio {MiniaudioLibrary.Version}");

        try
        {
            // 1. The backend. Without one, miniaudio picks the platform's best
            //    (WASAPI on Windows); the null backend needs no hardware.
            using var context = CreateContext(useNullBackend);
            var device = context.GetPlaybackDevices().FirstOrDefault(d => d.IsDefault);
            Console.WriteLine($"Backend: {context.Backend}, default device: {device.Name ?? "(none)"}");

            // 2. The engine: miniaudio's mixer and spatializer. Normally it
            //    plays on its own device, watched for reroutes and losses;
            //    with --pull it only mixes when asked.
            using var engineTimeout = new CancellationTokenSource(BackendTimeout);
            using var engine = AudioEngine.Create(pull
                ? new AudioEngineDescription { NoDevice = true }
                : new AudioEngineDescription { Context = context, DeviceObserver = new DeviceWatcher() },
                engineTimeout.Token);
            Console.WriteLine($"Engine: {engine.Channels} ch @ {engine.SampleRate} Hz{(pull ? ", pulled by an AudioDevice" : "")}");

            using var output = pull ? PullThroughDevice(context, engine) : null;

            // 3. Sounds — from generated PCM, or streamed from a file.
            var exitCode = file is null ? PlayTone(engine) : PlayFile(engine, file);

            if ((output?.Fault ?? engine.Fault) is { } fault)
            {
                Console.Error.WriteLine($"A device callback failed: {fault}");
                return 1;
            }

            return exitCode;
        }
        catch (MiniaudioException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"The audio device did not open within {BackendTimeout.TotalSeconds} s.");
            return 1;
        }
    }

    // Opening the backend talks to the system's audio server. There may be
    // none that works (a headless machine), and some wait forever on a broken
    // one (PulseAudio behind a wedged WSLg server). Either way, say so and
    // play silently on the null backend — the wrapper never picks it on its
    // own.
    private static AudioContext CreateContext(bool useNullBackend)
    {
        var nullBackend = new AudioContextDescription { Backend = AudioBackend.Null };
        if (useNullBackend)
        {
            return AudioContext.Create(nullBackend);
        }

        using var timeout = new CancellationTokenSource(BackendTimeout);
        try
        {
            return AudioContext.Create(default, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"The audio backend did not answer within {BackendTimeout.TotalSeconds} s; using the null backend (no sound).");
            return AudioContext.Create(nullBackend);
        }
        catch (MiniaudioException e)
        {
            Console.Error.WriteLine($"No audio backend opened ({e.Result}); using the null backend (no sound).");
            return AudioContext.Create(nullBackend);
        }
    }

    private static readonly TimeSpan BackendTimeout = TimeSpan.FromSeconds(5);

    // A device whose audio thread asks the engine for each period. Anything
    // that renders audio can sit in an IAudioRenderer; the engine is one.
    private static AudioDevice PullThroughDevice(AudioContext context, AudioEngine engine)
    {
        using var timeout = new CancellationTokenSource(BackendTimeout);
        var device = AudioDevice.Create(
            context,
            new AudioDeviceDescription { Channels = engine.Channels, SampleRate = engine.SampleRate },
            new EngineRenderer(engine),
            timeout.Token);
        device.Start();
        return device;
    }

    private sealed class EngineRenderer(AudioEngine engine) : IAudioRenderer
    {
        private readonly DeviceWatcher _watcher = new();

        public void Render(Span<float> output, int channels) => engine.Read(output);

        public void OnNotification(AudioDeviceNotification notification) => _watcher.OnNotification(notification);
    }

    // Device notifications arrive on a miniaudio thread. A game would record
    // them for its own thread (or poll AudioEngine.IsStarted) rather than act
    // here; printing is enough for a tour.
    private sealed class DeviceWatcher : IAudioDeviceObserver
    {
        public void OnNotification(AudioDeviceNotification notification)
        {
            if (notification == AudioDeviceNotification.Rerouted)
            {
                Console.WriteLine("(output rerouted)");
            }
        }
    }

    private static int PlayTone(AudioEngine engine)
    {
        // A SoundAsset doesn't care where its samples come from: here one
        // second of sine, generated straight into native PCM. 440 Hz fits a
        // whole number of cycles in a second, so it loops seamlessly.
        var rate = engine.SampleRate;
        var pcm = PcmBuffer.Allocate((ulong)rate, channels: 1, rate);
        var samples = pcm.Samples;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.2f * MathF.Sin(2 * MathF.PI * 440 * i / rate);
        }

        using var tone = SoundAsset.FromPcm(pcm);
        using var sound = Sound.Create(engine, tone, new SoundDescription { Looping = true });

        // Fade, then Start: Play() would clear the fade (it resets a sound
        // for a fresh one-shot), and a new sound is already at frame 0.
        sound.Fade(0, 1, TimeSpan.FromMilliseconds(250));
        sound.Start();

        // 4. Drive the sound from a "game loop": pan sweeps left to right and
        //    pitch rises an octave over three seconds.
        Console.WriteLine("Playing a 440 Hz tone (pan left -> right, pitch up an octave)...");
        const int steps = 60;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            sound.Pan = -1 + (2 * t);
            sound.Pitch = 1 + t;
            Thread.Sleep(50);
        }

        sound.StopWithFade(TimeSpan.FromMilliseconds(100));
        Thread.Sleep(150);
        return 0;
    }

    private static int PlayFile(AudioEngine engine, string path)
    {
        // Streamed: the asset keeps the encoded bytes and the sound decodes
        // them as it plays, so a long track never sits in memory as PCM.
        using var track = SoundAsset.Stream(File.ReadAllBytes(path));
        using var sound = Sound.Create(engine, track);

        var length = track.LengthInFrames / (double)track.SampleRate;
        Console.WriteLine($"Playing {Path.GetFileName(path)} ({length:0.0} s)...");

        sound.Play();
        while (sound.IsPlaying)
        {
            Console.Write($"\r  {sound.CursorInFrames / (double)track.SampleRate,6:0.0} / {length:0.0} s");
            Thread.Sleep(100);
        }

        Console.WriteLine();
        return 0;
    }
}
