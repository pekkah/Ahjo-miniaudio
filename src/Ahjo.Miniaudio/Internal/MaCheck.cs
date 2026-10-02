using System.Runtime.CompilerServices;

using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio.Internal;

internal static class MaCheck
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfFailed(ma_result result, string operation)
    {
        if (result != ma_result.MA_SUCCESS)
        {
            Throw(result, operation);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(ma_result result, string operation) =>
        throw new MiniaudioException(result, operation);
}
