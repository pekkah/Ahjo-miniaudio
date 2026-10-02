using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ahjo.Miniaudio.Native;

[StructLayout(LayoutKind.Explicit)]
public partial struct ma_device_id
{
    [FieldOffset(0)]
    [NativeTypeName("ma_wchar_win32[64]")]
    public _wasapi_e__FixedBuffer wasapi;

    [FieldOffset(0)]
    [NativeTypeName("ma_uint8[16]")]
    public _dsound_e__FixedBuffer dsound;

    [FieldOffset(0)]
    [NativeTypeName("ma_uint32")]
    public uint winmm;

    [FieldOffset(0)]
    [NativeTypeName("char[256]")]
    public _alsa_e__FixedBuffer alsa;

    [FieldOffset(0)]
    [NativeTypeName("char[256]")]
    public _pulse_e__FixedBuffer pulse;

    [FieldOffset(0)]
    public int jack;

    [FieldOffset(0)]
    [NativeTypeName("char[256]")]
    public _coreaudio_e__FixedBuffer coreaudio;

    [FieldOffset(0)]
    [NativeTypeName("char[256]")]
    public _sndio_e__FixedBuffer sndio;

    [FieldOffset(0)]
    [NativeTypeName("char[256]")]
    public _audio4_e__FixedBuffer audio4;

    [FieldOffset(0)]
    [NativeTypeName("char[64]")]
    public _oss_e__FixedBuffer oss;

    [FieldOffset(0)]
    [NativeTypeName("ma_int32")]
    public int aaudio;

    [FieldOffset(0)]
    [NativeTypeName("ma_uint32")]
    public uint opensl;

    [FieldOffset(0)]
    [NativeTypeName("char[32]")]
    public _webaudio_e__FixedBuffer webaudio;

    [FieldOffset(0)]
    [NativeTypeName("__AnonymousRecord_miniaudio_L7063_C5")]
    public _custom_e__Union custom;

    [FieldOffset(0)]
    public int nullbackend;

    [StructLayout(LayoutKind.Explicit)]
    public unsafe partial struct _custom_e__Union
    {
        [FieldOffset(0)]
        public int i;

        [FieldOffset(0)]
        [NativeTypeName("char[256]")]
        public _s_e__FixedBuffer s;

        [FieldOffset(0)]
        public void* p;

        [InlineArray(256)]
        public partial struct _s_e__FixedBuffer
        {
            public sbyte e0;
        }
    }

    [InlineArray(64)]
    public partial struct _wasapi_e__FixedBuffer
    {
        public ushort e0;
    }

    [InlineArray(16)]
    public partial struct _dsound_e__FixedBuffer
    {
        public byte e0;
    }

    [InlineArray(256)]
    public partial struct _alsa_e__FixedBuffer
    {
        public sbyte e0;
    }

    [InlineArray(256)]
    public partial struct _pulse_e__FixedBuffer
    {
        public sbyte e0;
    }

    [InlineArray(256)]
    public partial struct _coreaudio_e__FixedBuffer
    {
        public sbyte e0;
    }

    [InlineArray(256)]
    public partial struct _sndio_e__FixedBuffer
    {
        public sbyte e0;
    }

    [InlineArray(256)]
    public partial struct _audio4_e__FixedBuffer
    {
        public sbyte e0;
    }

    [InlineArray(64)]
    public partial struct _oss_e__FixedBuffer
    {
        public sbyte e0;
    }

    [InlineArray(32)]
    public partial struct _webaudio_e__FixedBuffer
    {
        public sbyte e0;
    }
}
