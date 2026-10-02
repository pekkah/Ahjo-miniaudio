using System.Runtime.CompilerServices;

namespace Ahjo.Miniaudio.Native;

public unsafe partial struct ma_device_descriptor
{
    [NativeTypeName("const ma_device_id *")]
    public ma_device_id* pDeviceID;

    public ma_share_mode shareMode;

    public ma_format format;

    [NativeTypeName("ma_uint32")]
    public uint channels;

    [NativeTypeName("ma_uint32")]
    public uint sampleRate;

    [NativeTypeName("ma_channel[254]")]
    public _channelMap_e__FixedBuffer channelMap;

    [NativeTypeName("ma_uint32")]
    public uint periodSizeInFrames;

    [NativeTypeName("ma_uint32")]
    public uint periodSizeInMilliseconds;

    [NativeTypeName("ma_uint32")]
    public uint periodCount;

    [InlineArray(254)]
    public partial struct _channelMap_e__FixedBuffer
    {
        public byte e0;
    }
}
