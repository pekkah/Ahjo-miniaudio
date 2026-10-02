using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>
/// Facts about the loaded native miniaudio library.
/// </summary>
public static unsafe class MiniaudioLibrary
{
    /// <summary>
    /// The miniaudio version the loaded <c>ahjo_miniaudio</c> binary was
    /// compiled from, as reported by the binary itself.
    /// </summary>
    public static Version Version
    {
        get
        {
            uint major, minor, revision;
            Ma.ma_version(&major, &minor, &revision);
            return new Version((int)major, (int)minor, (int)revision);
        }
    }
}
