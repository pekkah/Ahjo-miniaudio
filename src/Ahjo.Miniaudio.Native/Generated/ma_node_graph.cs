namespace Ahjo.Miniaudio.Native;

public unsafe partial struct ma_node_graph
{
    public ma_node_base @base;

    public ma_node_base endpoint;

    public float* pProcessingCache;

    [NativeTypeName("ma_uint32")]
    public uint processingCacheFramesRemaining;

    [NativeTypeName("ma_uint32")]
    public uint processingSizeInFrames;

    [NativeTypeName("ma_bool32")]
    public uint isReading;

    public ma_stack* pPreMixStack;
}
