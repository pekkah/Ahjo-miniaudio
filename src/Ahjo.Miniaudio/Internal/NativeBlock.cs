using System.Runtime.InteropServices;

namespace Ahjo.Miniaudio.Internal;

/// <summary>
/// Native memory for miniaudio objects. An initialized miniaudio object must
/// not move (the audio thread holds pointers into it), so none of them live on
/// the managed heap.
/// </summary>
internal static unsafe class NativeBlock
{
    // 64 bytes covers every alignment miniaudio declares (MA_ATOMIC and
    // MA_SIMD_ALIGNMENT top out at 32).
    private const nuint Alignment = 64;

    public static T* Alloc<T>() where T : unmanaged => (T*)Alloc((nuint)sizeof(T));

    public static void* Alloc(nuint size)
    {
        var p = NativeMemory.AlignedAlloc(size == 0 ? 1 : size, Alignment);
        NativeMemory.Clear(p, size);
        return p;
    }

    /// <summary>Resizes a block from <see cref="Alloc(nuint)"/>; the contents up to the smaller size are kept, the rest is not cleared.</summary>
    public static void* Realloc(void* p, nuint size) => NativeMemory.AlignedRealloc(p, size == 0 ? 1 : size, Alignment);

    public static void Free(void* p) => NativeMemory.AlignedFree(p);
}
