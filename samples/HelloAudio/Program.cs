// HelloAudio — the smallest useful tour of Ahjo.Miniaudio.
//
//   HelloAudio                 play a 440 Hz tone that pans left → right
//   HelloAudio <file>          stream a WAV / FLAC / MP3 file to the end
//   HelloAudio ... --null      use miniaudio's null backend (no speakers
//                              needed; the device runs on a timer — CI uses this)
//
// The wrapper (Ahjo.Miniaudio) does not have a playback API yet, so this
// sample talks to the raw bindings in Ahjo.Miniaudio.Native: functions are
// on `Ma`, types and enum members keep their C names from miniaudio.h.
// The pattern to take away:
//
//   * miniaudio objects (ma_engine, ma_sound, ...) are caller-allocated and
//     must not move while initialized, so they live in native memory, never
//     on the managed heap.
//   * every *_init has a matching *_uninit; uninit in reverse order.
//   * everything here is setup-time code. Per-frame calls (set_pan,
//     set_pitch, ...) allocate nothing.

using System.Runtime.InteropServices;

using Ahjo.Miniaudio;
using Ahjo.Miniaudio.Native;

namespace HelloAudio;

internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        var useNullBackend = args.Contains("--null");
        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        Console.WriteLine($"miniaudio {MiniaudioLibrary.Version}");

        ma_context* context = null;
        var engine = Alloc<ma_engine>();
        try
        {
            // 1. The engine. With no config, miniaudio picks the platform's
            //    best backend (WASAPI on Windows) and the default device.
            var engineConfig = Ma.ma_engine_config_init();
            if (useNullBackend)
            {
                context = Alloc<ma_context>();
                var backend = ma_backend.ma_backend_null;
                var contextConfig = Ma.ma_context_config_init();
                Check(Ma.ma_context_init(&backend, 1, &contextConfig, context), "ma_context_init");
                engineConfig.pContext = context;
            }

            Check(Ma.ma_engine_init(&engineConfig, engine), "ma_engine_init");
            try
            {
                PrintDevice(engine);

                // 2. A sound — from a file, or from a generated waveform.
                return file is null ? PlayTone(engine) : PlayFile(engine, file);
            }
            finally
            {
                Ma.ma_engine_uninit(engine);
            }
        }
        catch (MiniaudioException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        finally
        {
            if (context is not null)
            {
                Ma.ma_context_uninit(context);
                NativeMemory.AlignedFree(context);
            }

            NativeMemory.AlignedFree(engine);
        }
    }

    private static int PlayTone(ma_engine* engine)
    {
        // Any miniaudio data source can back a sound. A waveform generator
        // is the simplest one and needs no asset.
        var waveform = Alloc<ma_waveform>();
        var sound = Alloc<ma_sound>();
        try
        {
            var config = Ma.ma_waveform_config_init(
                ma_format.ma_format_f32,
                Ma.ma_engine_get_channels(engine),
                Ma.ma_engine_get_sample_rate(engine),
                ma_waveform_type.ma_waveform_type_sine,
                amplitude: 0.2,
                frequency: 440);
            Check(Ma.ma_waveform_init(&config, waveform), "ma_waveform_init");
            try
            {
                Check(Ma.ma_sound_init_from_data_source(engine, waveform, 0, null, sound), "ma_sound_init_from_data_source");
                try
                {
                    Ma.ma_sound_set_fade_in_milliseconds(sound, 0, 1, 250);
                    Check(Ma.ma_sound_start(sound), "ma_sound_start");

                    // 3. Drive the sound from a "game loop": pan sweeps left
                    //    to right and pitch rises an octave over three seconds.
                    Console.WriteLine("Playing a 440 Hz tone (pan left -> right, pitch up an octave)...");
                    const int steps = 60;
                    for (var i = 0; i <= steps; i++)
                    {
                        var t = i / (float)steps;
                        Ma.ma_sound_set_pan(sound, -1 + 2 * t);
                        Ma.ma_sound_set_pitch(sound, 1 + t);
                        Thread.Sleep(50);
                    }
                }
                finally
                {
                    Ma.ma_sound_uninit(sound);
                }
            }
            finally
            {
                Ma.ma_waveform_uninit(waveform);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(sound);
            NativeMemory.AlignedFree(waveform);
        }

        return 0;
    }

    private static int PlayFile(ma_engine* engine, string path)
    {
        var sound = Alloc<ma_sound>();
        try
        {
            // The _w variant takes the path as UTF-16, which is exactly what
            // a .NET string already is on Windows — no conversion, no copy.
            // STREAM decodes on the fly instead of loading the whole file.
            fixed (char* p = path)
            {
                Check(Ma.ma_sound_init_from_file_w(engine, (ushort*)p, (uint)ma_sound_flags.MA_SOUND_FLAG_STREAM, null, null, sound),
                    $"ma_sound_init_from_file_w(\"{path}\")");
            }

            try
            {
                float length;
                Ma.ma_sound_get_length_in_seconds(sound, &length);
                Console.WriteLine($"Playing {Path.GetFileName(path)} ({length:0.0} s)...");

                Check(Ma.ma_sound_start(sound), "ma_sound_start");
                while (Ma.ma_sound_at_end(sound) == 0)
                {
                    float cursor;
                    Ma.ma_sound_get_cursor_in_seconds(sound, &cursor);
                    Console.Write($"\r  {cursor,6:0.0} / {length:0.0} s");
                    Thread.Sleep(100);
                }

                Console.WriteLine();
            }
            finally
            {
                Ma.ma_sound_uninit(sound);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(sound);
        }

        return 0;
    }

    private static void PrintDevice(ma_engine* engine)
    {
        var device = Ma.ma_engine_get_device(engine);
        ma_device_info info;
        Check(Ma.ma_device_get_info(device, ma_device_type.ma_device_type_playback, &info), "ma_device_get_info");

        var backend = Marshal.PtrToStringUTF8((nint)Ma.ma_get_backend_name(device->pContext->backend));
        var name = Marshal.PtrToStringUTF8((nint)(&info.name));
        Console.WriteLine($"Backend: {backend}, device: {name}, " +
                          $"{Ma.ma_engine_get_channels(engine)} ch @ {Ma.ma_engine_get_sample_rate(engine)} Hz");
    }

    // miniaudio objects must stay put while initialized, so they live in
    // native memory. 64 bytes covers every alignment miniaudio declares.
    private static T* Alloc<T>() where T : unmanaged
    {
        var p = (T*)NativeMemory.AlignedAlloc((nuint)sizeof(T), 64);
        NativeMemory.Clear(p, (nuint)sizeof(T));
        return p;
    }

    private static void Check(ma_result result, string call)
    {
        if (result != ma_result.MA_SUCCESS)
        {
            throw new MiniaudioException($"{call} failed: {Marshal.PtrToStringUTF8((nint)Ma.ma_result_description(result))}");
        }
    }

    private sealed class MiniaudioException(string message) : Exception(message);
}
