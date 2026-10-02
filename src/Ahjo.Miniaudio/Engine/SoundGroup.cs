using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>
/// A mixing bus (<c>ma_sound_group</c>): sounds created into it are mixed
/// through it, so its volume, pan and pitch apply to all of them — e.g. Music,
/// Effects and UI buses under one master. Groups nest.
/// </summary>
/// <remarks>
/// Disposing a group detaches the sounds in it (they go silent) and does not
/// dispose them. Its engine's <see cref="AudioEngine.Dispose"/> disposes it.
/// </remarks>
public sealed unsafe class SoundGroup : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly LinkedListNode<IDisposable> _registration;
    private ma_sound* _group;

    private SoundGroup(AudioEngine engine, ma_sound* group)
    {
        _engine = engine;
        _group = group;
        _registration = engine.Register(this);
    }

    /// <summary>Creates a group on <paramref name="engine"/>, mixed into <paramref name="parent"/> (or the engine's output).</summary>
    public static SoundGroup Create(AudioEngine engine, SoundGroup? parent = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var group = NativeBlock.Alloc<ma_sound>();
        var result = Ma.ma_sound_group_init(engine.Native, 0, parent is null ? null : parent.Native, group);
        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(group);
            throw new MiniaudioException(result, "ma_sound_group_init");
        }

        result = Ma.ma_sound_group_start(group);
        if (result != ma_result.MA_SUCCESS)
        {
            Ma.ma_sound_group_uninit(group);
            NativeBlock.Free(group);
            throw new MiniaudioException(result, "ma_sound_group_start");
        }

        return new SoundGroup(engine, group);
    }

    /// <summary>Linear volume applied to everything in the group.</summary>
    public float Volume
    {
        get => Ma.ma_sound_group_get_volume(Native);
        set => Ma.ma_sound_group_set_volume(Native, value);
    }

    /// <summary>Stereo pan, −1 (left) to 1 (right).</summary>
    public float Pan
    {
        get => Ma.ma_sound_group_get_pan(Native);
        set => Ma.ma_sound_group_set_pan(Native, value);
    }

    /// <summary>Pitch multiplier applied to everything in the group.</summary>
    public float Pitch
    {
        get => Ma.ma_sound_group_get_pitch(Native);
        set => Ma.ma_sound_group_set_pitch(Native, value);
    }

    /// <summary>Whether the group is passing audio. Groups start when created.</summary>
    public bool IsPlaying => Ma.ma_sound_group_is_playing(Native) != 0;

    /// <summary>Resumes the group (and so every sound in it).</summary>
    public void Start() => MaCheck.ThrowIfFailed(Ma.ma_sound_group_start(Native), "ma_sound_group_start");

    /// <summary>Pauses the group: every sound in it goes silent, keeping its own state.</summary>
    public void Stop() => MaCheck.ThrowIfFailed(Ma.ma_sound_group_stop(Native), "ma_sound_group_stop");

    internal ma_sound* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_group == null, this);
            return _group;
        }
    }

    /// <summary>Removes the group from the mix.</summary>
    public void Dispose()
    {
        if (_group == null)
        {
            return;
        }

        _engine.Unregister(_registration);
        Ma.ma_sound_group_uninit(_group);
        NativeBlock.Free(_group);
        _group = null;
    }
}
