namespace Ahjo.Miniaudio.Native;

public partial struct ma_file_info
{
    [NativeTypeName("ma_uint64")]
    public ulong sizeInBytes;
}
