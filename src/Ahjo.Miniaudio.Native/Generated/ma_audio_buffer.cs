using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ahjo.Miniaudio.Native;

public partial struct ma_audio_buffer
{
    public ma_audio_buffer_ref @ref;

    public ma_allocation_callbacks allocationCallbacks;

    [NativeTypeName("ma_bool32")]
    public uint ownsData;

    [NativeTypeName("ma_uint8[1]")]
    public __pExtraData_e__FixedBuffer _pExtraData;

    public partial struct __pExtraData_e__FixedBuffer
    {
        public byte e0;

        [UnscopedRef]
        public ref byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Unsafe.Add(ref e0, index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnscopedRef]
        public Span<byte> AsSpan(int length) => MemoryMarshal.CreateSpan(ref e0, length);
    }
}
