using System.Runtime.CompilerServices;

namespace Ahjo.Miniaudio.Native;

public unsafe partial struct ma_log
{
    [NativeTypeName("ma_log_callback[4]")]
    public _callbacks_e__FixedBuffer callbacks;

    [NativeTypeName("ma_uint32")]
    public uint callbackCount;

    public ma_allocation_callbacks allocationCallbacks;

    [NativeTypeName("ma_mutex")]
    public void* @lock;

    [InlineArray(4)]
    public partial struct _callbacks_e__FixedBuffer
    {
        public ma_log_callback e0;
    }
}
