namespace Ahjo.Miniaudio.Native;

public partial struct ma_node_graph_config
{
    [NativeTypeName("ma_uint32")]
    public uint channels;

    [NativeTypeName("ma_uint32")]
    public uint processingSizeInFrames;

    [NativeTypeName("size_t")]
    public nuint preMixStackSizeInBytes;
}
