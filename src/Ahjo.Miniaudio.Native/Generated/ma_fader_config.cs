namespace Ahjo.Miniaudio.Native;

public partial struct ma_fader_config
{
    public ma_format format;

    [NativeTypeName("ma_uint32")]
    public uint channels;

    [NativeTypeName("ma_uint32")]
    public uint sampleRate;
}
