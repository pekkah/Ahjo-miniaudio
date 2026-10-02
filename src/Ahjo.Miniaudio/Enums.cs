using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

// Every value equals its ma_* counterpart, so the wrapper converts with a cast.

/// <summary>An audio backend miniaudio can drive. Values match <see cref="ma_backend"/>.</summary>
public enum AudioBackend
{
    /// <summary>Windows Audio Session API — the default on Windows.</summary>
    Wasapi = ma_backend.ma_backend_wasapi,
    /// <summary>DirectSound.</summary>
    DirectSound = ma_backend.ma_backend_dsound,
    /// <summary>WinMM.</summary>
    WinMM = ma_backend.ma_backend_winmm,
    /// <summary>Core Audio (Apple).</summary>
    CoreAudio = ma_backend.ma_backend_coreaudio,
    /// <summary>sndio.</summary>
    Sndio = ma_backend.ma_backend_sndio,
    /// <summary>audio(4).</summary>
    Audio4 = ma_backend.ma_backend_audio4,
    /// <summary>OSS.</summary>
    Oss = ma_backend.ma_backend_oss,
    /// <summary>PulseAudio.</summary>
    PulseAudio = ma_backend.ma_backend_pulseaudio,
    /// <summary>ALSA.</summary>
    Alsa = ma_backend.ma_backend_alsa,
    /// <summary>JACK.</summary>
    Jack = ma_backend.ma_backend_jack,
    /// <summary>AAudio (Android).</summary>
    AAudio = ma_backend.ma_backend_aaudio,
    /// <summary>OpenSL|ES (Android).</summary>
    OpenSL = ma_backend.ma_backend_opensl,
    /// <summary>Web Audio.</summary>
    WebAudio = ma_backend.ma_backend_webaudio,
    /// <summary>A custom backend.</summary>
    Custom = ma_backend.ma_backend_custom,
    /// <summary>
    /// No hardware: a simulated device on a timer thread. Runs the full device
    /// lifecycle, including the render callback, on machines without audio
    /// output (CI).
    /// </summary>
    Null = ma_backend.ma_backend_null,
}

/// <summary>A device state change, delivered on a miniaudio thread. Values match <see cref="ma_device_notification_type"/>.</summary>
public enum AudioDeviceNotification
{
    /// <summary>The device started.</summary>
    Started = ma_device_notification_type.ma_device_notification_type_started,
    /// <summary>The device stopped — by request, or because the backend lost it.</summary>
    Stopped = ma_device_notification_type.ma_device_notification_type_stopped,
    /// <summary>The backend moved the stream to another endpoint (e.g. headphones plugged in).</summary>
    Rerouted = ma_device_notification_type.ma_device_notification_type_rerouted,
    /// <summary>The OS interrupted the stream (mobile platforms).</summary>
    InterruptionBegan = ma_device_notification_type.ma_device_notification_type_interruption_began,
    /// <summary>The OS interruption ended.</summary>
    InterruptionEnded = ma_device_notification_type.ma_device_notification_type_interruption_ended,
    /// <summary>The device was unlocked by a user gesture (Web Audio).</summary>
    Unlocked = ma_device_notification_type.ma_device_notification_type_unlocked,
}

/// <summary>How a spatialized sound's gain falls off with distance. Values match <see cref="ma_attenuation_model"/>.</summary>
public enum AttenuationModel
{
    /// <summary>No distance attenuation.</summary>
    None = ma_attenuation_model.ma_attenuation_model_none,
    /// <summary>Inverse distance (OpenAL's default).</summary>
    Inverse = ma_attenuation_model.ma_attenuation_model_inverse,
    /// <summary>Linear between min and max distance.</summary>
    Linear = ma_attenuation_model.ma_attenuation_model_linear,
    /// <summary>Exponential.</summary>
    Exponential = ma_attenuation_model.ma_attenuation_model_exponential,
}

/// <summary>What a spatialized sound's position is relative to. Values match <see cref="ma_positioning"/>.</summary>
public enum Positioning
{
    /// <summary>World space.</summary>
    Absolute = ma_positioning.ma_positioning_absolute,
    /// <summary>Relative to the listener (e.g. a sound attached to the camera).</summary>
    Relative = ma_positioning.ma_positioning_relative,
}
