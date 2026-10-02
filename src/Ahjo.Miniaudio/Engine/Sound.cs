using System.Numerics;

using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>Options for <see cref="Sound.Create"/>. The default is valid: a 2D, non-looping sound on the engine's output.</summary>
public readonly record struct SoundDescription
{
    /// <summary>The bus to mix through; <see langword="null"/> for the engine's output.</summary>
    public SoundGroup? Group { get; init; }

    /// <summary>Loop at the end instead of stopping.</summary>
    public bool Looping { get; init; }

    /// <summary>
    /// Spatialize against the engine's listeners using <see cref="Sound.Position"/>.
    /// Off by default: a 2D sound (music, UI) ignores position and plays as-is.
    /// </summary>
    public bool Spatialized { get; init; }
}

/// <summary>
/// One playable voice (<c>ma_sound</c>) over a <see cref="SoundAsset"/>.
/// </summary>
/// <remarks>
/// <para><b>Create at setup, play per frame.</b> Creating a sound allocates
/// (one managed object, one native block, one data-source init); every other
/// member allocates nothing. Hold a sound per emitter, or a
/// <see cref="SoundPool"/> per effect, and <see cref="Play"/> it — never create a
/// sound per gunshot.</para>
/// <para>Created stopped. Its engine's <see cref="AudioEngine.Dispose"/>
/// disposes it.</para>
/// </remarks>
public sealed unsafe class Sound : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly SoundAsset _asset;
    private readonly LinkedListNode<IDisposable> _registration;
    private readonly uint _engineSampleRate;
    private ma_sound* _sound;
    private void* _source;

    private Sound(AudioEngine engine, SoundAsset asset, ma_sound* sound, void* source)
    {
        _engine = engine;
        _asset = asset;
        _sound = sound;
        _source = source;
        _engineSampleRate = (uint)engine.SampleRate;
        _registration = engine.Register(this);
    }

    /// <summary>Creates a stopped sound playing <paramref name="asset"/> on <paramref name="engine"/>.</summary>
    /// <exception cref="MiniaudioException">miniaudio could not initialize the sound or its data source.</exception>
    public static Sound Create(AudioEngine engine, SoundAsset asset, in SoundDescription description = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(asset);
        var native = engine.Native;
        var group = description.Group is null ? null : description.Group.Native;

        asset.AddReference();
        void* source = null;
        ma_sound* sound = null;
        try
        {
            source = InitSource(asset);
            sound = NativeBlock.Alloc<ma_sound>();
            var flags = description.Spatialized ? 0u : (uint)ma_sound_flags.MA_SOUND_FLAG_NO_SPATIALIZATION;
            var result = Ma.ma_sound_init_from_data_source(native, source, flags, group, sound);
            if (result != ma_result.MA_SUCCESS)
            {
                NativeBlock.Free(sound);
                sound = null;
                throw new MiniaudioException(result, "ma_sound_init_from_data_source");
            }

            if (description.Looping)
            {
                Ma.ma_sound_set_looping(sound, 1);
            }

            return new Sound(engine, asset, sound, source);
        }
        catch
        {
            if (sound != null)
            {
                Ma.ma_sound_uninit(sound);
                NativeBlock.Free(sound);
            }

            UninitSource(asset, source);
            asset.Release();
            throw;
        }
    }

    // A decoded asset gets a cursor over the shared PCM; a streamed one, a
    // decoder over the shared bytes. Either is an ma_data_source.
    private static void* InitSource(SoundAsset asset)
    {
        if (asset.IsStreamed)
        {
            var decoder = NativeBlock.Alloc<ma_decoder>();
            var result = AudioDecoder.Init(asset.Encoded, asset.EncodedSize, default, decoder);
            if (result != ma_result.MA_SUCCESS)
            {
                NativeBlock.Free(decoder);
                throw new MiniaudioException(result, "ma_decoder_init_memory");
            }

            return decoder;
        }

        var buffer = NativeBlock.Alloc<ma_audio_buffer_ref>();
        var initResult = Ma.ma_audio_buffer_ref_init(ma_format.ma_format_f32, (uint)asset.Channels, asset.Pcm, asset.LengthInFrames, buffer);
        if (initResult != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(buffer);
            throw new MiniaudioException(initResult, "ma_audio_buffer_ref_init");
        }

        // ma_audio_buffer_ref_init leaves sampleRate 0 ("TODO: Version 0.12"),
        // which ma_sound reads as "the engine's rate" — the asset would play
        // at the wrong speed on any engine whose rate differs. The field is
        // public; set it before the sound reads it.
        buffer->sampleRate = (uint)asset.SampleRate;
        return buffer;
    }

    private static void UninitSource(SoundAsset asset, void* source)
    {
        if (source == null)
        {
            return;
        }

        if (asset.IsStreamed)
        {
            Ma.ma_decoder_uninit((ma_decoder*)source);
        }
        else
        {
            Ma.ma_audio_buffer_ref_uninit((ma_audio_buffer_ref*)source);
        }

        NativeBlock.Free(source);
    }

    /// <summary>The asset this sound plays.</summary>
    public SoundAsset Asset => _asset;

    /// <summary>
    /// Plays from the start: clears any fade or scheduled stop (so a sound
    /// stopped with <see cref="StopWithFade"/> is audible again), rewinds,
    /// and starts. What a one-shot effect calls.
    /// </summary>
    /// <remarks>
    /// Because it clears the fade, a <see cref="Fade"/> set before
    /// <c>Play</c> is lost, and one set after it can miss the first period.
    /// To fade a sound in, call <see cref="Fade"/> and then <see cref="Start"/>
    /// (after <see cref="Seek"/>(0) if it has played before).
    /// </remarks>
    public void Play()
    {
        var sound = Native;
        Ma.ma_sound_reset_stop_time_and_fade(sound);
        MaCheck.ThrowIfFailed(Ma.ma_sound_seek_to_pcm_frame(sound, 0), "ma_sound_seek_to_pcm_frame");
        MaCheck.ThrowIfFailed(Ma.ma_sound_start(sound), "ma_sound_start");
    }

    /// <summary>Starts (or resumes) from the current cursor.</summary>
    public void Start() => MaCheck.ThrowIfFailed(Ma.ma_sound_start(Native), "ma_sound_start");

    /// <summary>Pauses at the current cursor; <see cref="Start"/> resumes.</summary>
    public void Stop() => MaCheck.ThrowIfFailed(Ma.ma_sound_stop(Native), "ma_sound_stop");

    /// <summary>Fades to silence over <paramref name="duration"/>, then stops.</summary>
    public void StopWithFade(TimeSpan duration) =>
        MaCheck.ThrowIfFailed(
            Ma.ma_sound_stop_with_fade_in_pcm_frames(Native, Units.ToFrames(duration, _engineSampleRate)),
            "ma_sound_stop_with_fade_in_pcm_frames");

    /// <summary>
    /// Fades volume from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="duration"/>, multiplying <see cref="Volume"/>. Pass −1 as
    /// <paramref name="from"/> to start from the current fade level.
    /// </summary>
    public void Fade(float from, float to, TimeSpan duration) =>
        Ma.ma_sound_set_fade_in_pcm_frames(Native, from, to, Units.ToFrames(duration, _engineSampleRate));

    /// <summary>Moves the cursor to <paramref name="frame"/> (in the asset's frames).</summary>
    public void Seek(ulong frame) =>
        MaCheck.ThrowIfFailed(Ma.ma_sound_seek_to_pcm_frame(Native, frame), "ma_sound_seek_to_pcm_frame");

    /// <summary>Whether the sound is playing. False once a non-looping sound reaches its end.</summary>
    public bool IsPlaying => Ma.ma_sound_is_playing(Native) != 0;

    /// <summary>Whether a non-looping sound has played to its end.</summary>
    public bool AtEnd => Ma.ma_sound_at_end(Native) != 0;

    /// <summary>The playback position, in the asset's frames.</summary>
    public ulong CursorInFrames
    {
        get
        {
            ulong cursor;
            MaCheck.ThrowIfFailed(Ma.ma_sound_get_cursor_in_pcm_frames(Native, &cursor), "ma_sound_get_cursor_in_pcm_frames");
            return cursor;
        }
    }

    /// <summary>The length, in the asset's frames; 0 if a stream cannot tell.</summary>
    public ulong LengthInFrames
    {
        get
        {
            ulong length;
            return Ma.ma_sound_get_length_in_pcm_frames(Native, &length) == ma_result.MA_SUCCESS ? length : 0;
        }
    }

    /// <summary>Linear volume (1 = unity).</summary>
    public float Volume
    {
        get => Ma.ma_sound_get_volume(Native);
        set => Ma.ma_sound_set_volume(Native, value);
    }

    /// <summary>Stereo pan, −1 (left) to 1 (right).</summary>
    public float Pan
    {
        get => Ma.ma_sound_get_pan(Native);
        set => Ma.ma_sound_set_pan(Native, value);
    }

    /// <summary>Pitch multiplier (1 = original; 2 = an octave up). Also changes speed.</summary>
    public float Pitch
    {
        get => Ma.ma_sound_get_pitch(Native);
        set => Ma.ma_sound_set_pitch(Native, value);
    }

    /// <summary>Whether the sound loops at its end.</summary>
    public bool Looping
    {
        get => Ma.ma_sound_is_looping(Native) != 0;
        set => Ma.ma_sound_set_looping(Native, Units.Bool(value));
    }

    /// <summary>Whether the sound is spatialized against the listeners (<see cref="SoundDescription.Spatialized"/>).</summary>
    public bool Spatialized
    {
        get => Ma.ma_sound_is_spatialization_enabled(Native) != 0;
        set => Ma.ma_sound_set_spatialization_enabled(Native, Units.Bool(value));
    }

    /// <summary>Position, in world space or relative to the listener (<see cref="Positioning"/>). Spatialized sounds only.</summary>
    public Vector3 Position
    {
        get => Units.ToVector3(Ma.ma_sound_get_position(Native));
        set => Ma.ma_sound_set_position(Native, value.X, value.Y, value.Z);
    }

    /// <summary>The direction the sound faces, for its cone (<see cref="SetCone"/>).</summary>
    public Vector3 Direction
    {
        get => Units.ToVector3(Ma.ma_sound_get_direction(Native));
        set => Ma.ma_sound_set_direction(Native, value.X, value.Y, value.Z);
    }

    /// <summary>Velocity in units per second; drives the doppler effect.</summary>
    public Vector3 Velocity
    {
        get => Units.ToVector3(Ma.ma_sound_get_velocity(Native));
        set => Ma.ma_sound_set_velocity(Native, value.X, value.Y, value.Z);
    }

    /// <summary>Whether <see cref="Position"/> is in world space or relative to the listener.</summary>
    public Positioning Positioning
    {
        get => (Positioning)Ma.ma_sound_get_positioning(Native);
        set => Ma.ma_sound_set_positioning(Native, (ma_positioning)value);
    }

    /// <summary>How gain falls off with distance.</summary>
    public AttenuationModel AttenuationModel
    {
        get => (AttenuationModel)Ma.ma_sound_get_attenuation_model(Native);
        set => Ma.ma_sound_set_attenuation_model(Native, (ma_attenuation_model)value);
    }

    /// <summary>How quickly gain falls off under <see cref="AttenuationModel"/>.</summary>
    public float Rolloff
    {
        get => Ma.ma_sound_get_rolloff(Native);
        set => Ma.ma_sound_set_rolloff(Native, value);
    }

    /// <summary>Distance within which the sound is at full gain.</summary>
    public float MinDistance
    {
        get => Ma.ma_sound_get_min_distance(Native);
        set => Ma.ma_sound_set_min_distance(Native, value);
    }

    /// <summary>Distance beyond which the sound attenuates no further.</summary>
    public float MaxDistance
    {
        get => Ma.ma_sound_get_max_distance(Native);
        set => Ma.ma_sound_set_max_distance(Native, value);
    }

    /// <summary>Doppler strength; 0 disables it.</summary>
    public float DopplerFactor
    {
        get => Ma.ma_sound_get_doppler_factor(Native);
        set => Ma.ma_sound_set_doppler_factor(Native, value);
    }

    /// <summary>
    /// A directional emission cone around <see cref="Direction"/>: full gain inside
    /// <paramref name="innerAngle"/>, <paramref name="outerGain"/> outside
    /// <paramref name="outerAngle"/> (radians).
    /// </summary>
    public void SetCone(float innerAngle, float outerAngle, float outerGain) =>
        Ma.ma_sound_set_cone(Native, innerAngle, outerAngle, outerGain);

    private ma_sound* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_sound == null, this);
            return _sound;
        }
    }

    /// <summary>Removes the sound from the mix and releases its asset reference.</summary>
    public void Dispose()
    {
        if (_sound == null)
        {
            return;
        }

        _engine.Unregister(_registration);

        // The sound reads its data source until uninit returns.
        Ma.ma_sound_uninit(_sound);
        NativeBlock.Free(_sound);
        _sound = null;
        UninitSource(_asset, _source);
        _source = null;
        _asset.Release();
    }
}
