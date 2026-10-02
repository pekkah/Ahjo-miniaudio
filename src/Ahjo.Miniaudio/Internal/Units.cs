using System.Numerics;
using System.Runtime.CompilerServices;

using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio.Internal;

internal static class Units
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ToVector3(ma_vec3f v) => new(v.x, v.y, v.z);

    /// <summary>A duration in PCM frames at <paramref name="sampleRate"/>; negative durations clamp to 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToFrames(TimeSpan duration, uint sampleRate) =>
        duration <= TimeSpan.Zero ? 0 : (ulong)(duration.Ticks * (double)sampleRate / TimeSpan.TicksPerSecond);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Bool(bool value) => value ? 1u : 0u;
}
