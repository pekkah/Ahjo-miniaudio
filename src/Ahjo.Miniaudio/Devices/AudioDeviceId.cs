using System.Runtime.InteropServices;

using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>
/// Identifies one device on one backend, as reported by
/// <see cref="AudioContext.GetPlaybackDevices"/>. Pass it back in a device or
/// engine description to open that device instead of the default.
/// </summary>
/// <remarks>Opaque and backend-specific; only meaningful to the context (backend) that produced it.</remarks>
public readonly struct AudioDeviceId : IEquatable<AudioDeviceId>
{
    internal readonly ma_device_id Native;

    internal AudioDeviceId(in ma_device_id native) => Native = native;

    // ma_device_id_equal is a byte-wise compare over the whole union, so the
    // same comparison runs here without a native call.
    private static ReadOnlySpan<byte> Bytes(in AudioDeviceId id) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<ma_device_id>(in id.Native));

    /// <inheritdoc />
    public bool Equals(AudioDeviceId other) => Bytes(in this).SequenceEqual(Bytes(in other));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AudioDeviceId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes(in this));
        return hash.ToHashCode();
    }

    /// <summary>Byte-wise equality.</summary>
    public static bool operator ==(AudioDeviceId left, AudioDeviceId right) => left.Equals(right);

    /// <summary>Byte-wise inequality.</summary>
    public static bool operator !=(AudioDeviceId left, AudioDeviceId right) => !left.Equals(right);
}

/// <summary>A playback device a context can open.</summary>
/// <param name="Name">The backend's display name for the device.</param>
/// <param name="Id">Pass to <see cref="AudioDeviceDescription.PlaybackDevice"/> or <see cref="AudioEngineDescription.PlaybackDevice"/>.</param>
/// <param name="IsDefault">Whether this is the system default playback device.</param>
public readonly record struct AudioDeviceInfo(string Name, AudioDeviceId Id, bool IsDefault);
