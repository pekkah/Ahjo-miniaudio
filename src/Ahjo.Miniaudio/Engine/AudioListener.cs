using System.Numerics;

using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>
/// One of an <see cref="AudioEngine"/>'s listeners: the ears spatialized
/// sounds are heard from. A handle (engine + index); copying it is free and
/// every member allocates nothing.
/// </summary>
/// <remarks>
/// Right-handed, like <c>System.Numerics</c>: the default listener sits at
/// the origin looking down −Z with +Y up.
/// </remarks>
public readonly unsafe struct AudioListener
{
    private readonly AudioEngine _engine;

    internal AudioListener(AudioEngine engine, int index)
    {
        _engine = engine;
        Index = index;
    }

    /// <summary>The listener's index on its engine.</summary>
    public int Index { get; }

    /// <summary>World-space position.</summary>
    public Vector3 Position
    {
        get => Units.ToVector3(Ma.ma_engine_listener_get_position(Engine, (uint)Index));
        set => Ma.ma_engine_listener_set_position(Engine, (uint)Index, value.X, value.Y, value.Z);
    }

    /// <summary>The direction the listener faces.</summary>
    public Vector3 Direction
    {
        get => Units.ToVector3(Ma.ma_engine_listener_get_direction(Engine, (uint)Index));
        set => Ma.ma_engine_listener_set_direction(Engine, (uint)Index, value.X, value.Y, value.Z);
    }

    /// <summary>The listener's up vector.</summary>
    public Vector3 WorldUp
    {
        get => Units.ToVector3(Ma.ma_engine_listener_get_world_up(Engine, (uint)Index));
        set => Ma.ma_engine_listener_set_world_up(Engine, (uint)Index, value.X, value.Y, value.Z);
    }

    /// <summary>Velocity in units per second; drives the doppler effect.</summary>
    public Vector3 Velocity
    {
        get => Units.ToVector3(Ma.ma_engine_listener_get_velocity(Engine, (uint)Index));
        set => Ma.ma_engine_listener_set_velocity(Engine, (uint)Index, value.X, value.Y, value.Z);
    }

    /// <summary>Whether sounds are spatialized against this listener.</summary>
    public bool Enabled
    {
        get => Ma.ma_engine_listener_is_enabled(Engine, (uint)Index) != 0;
        set => Ma.ma_engine_listener_set_enabled(Engine, (uint)Index, Units.Bool(value));
    }

    /// <summary>
    /// A directional hearing cone: full gain inside <paramref name="innerAngle"/>,
    /// <paramref name="outerGain"/> outside <paramref name="outerAngle"/> (radians).
    /// </summary>
    public void SetCone(float innerAngle, float outerAngle, float outerGain) =>
        Ma.ma_engine_listener_set_cone(Engine, (uint)Index, innerAngle, outerAngle, outerGain);

    /// <summary>
    /// Places the listener at a world transform — typically the active camera's.
    /// </summary>
    /// <param name="worldTransform">
    /// A <c>System.Numerics</c> (row-vector) transform: translation in
    /// <c>M41..M43</c>, local −Z forward (the negated third row) and +Y up (the
    /// second row). Scale is normalized away.
    /// </param>
    public void SetPose(in Matrix4x4 worldTransform)
    {
        var engine = Engine;
        var index = (uint)Index;
        var forward = Vector3.Normalize(new Vector3(-worldTransform.M31, -worldTransform.M32, -worldTransform.M33));
        var up = Vector3.Normalize(new Vector3(worldTransform.M21, worldTransform.M22, worldTransform.M23));
        Ma.ma_engine_listener_set_position(engine, index, worldTransform.M41, worldTransform.M42, worldTransform.M43);
        Ma.ma_engine_listener_set_direction(engine, index, forward.X, forward.Y, forward.Z);
        Ma.ma_engine_listener_set_world_up(engine, index, up.X, up.Y, up.Z);
    }

    private ma_engine* Engine
    {
        get
        {
            if (_engine is null)
            {
                throw new InvalidOperationException("This listener is default(AudioListener); get one from AudioEngine.Listener or GetListener.");
            }

            return _engine.Native;
        }
    }
}
