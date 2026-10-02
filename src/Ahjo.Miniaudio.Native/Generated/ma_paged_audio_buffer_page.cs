using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ahjo.Miniaudio.Native;

public unsafe partial struct ma_paged_audio_buffer_page
{
    public ma_paged_audio_buffer_page* pNext;

    [NativeTypeName("ma_uint64")]
    public ulong sizeInFrames;

    [NativeTypeName("ma_uint8[1]")]
    public _pAudioData_e__FixedBuffer pAudioData;

    public partial struct _pAudioData_e__FixedBuffer
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
