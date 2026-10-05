using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio.Internal;

/// <summary>
/// Opens a device (or an engine's device) without a caller-supplied context,
/// the way <c>ma_device_init_ex</c> does: for each backend in priority order,
/// initialize a context and try to open on it; on failure uninitialize it
/// and move on. Done here rather than by miniaudio so that the null backend
/// is tried only when the caller allowed it — miniaudio always ends its list
/// with it, which turns "no audio" into silent success.
/// </summary>
internal static unsafe class BackendFallback
{
    public const string AllowNullHint =
        "No audio backend could be opened. Set AllowNullBackend to fall back to the null backend (no sound).";

    /// <summary>
    /// Tries <paramref name="backends"/> in order on the uninitialized
    /// <paramref name="context"/>. On success the context is initialized and
    /// <paramref name="open"/> has succeeded on it; on failure everything is
    /// uninitialized again.
    /// </summary>
    /// <returns><c>MA_SUCCESS</c>; otherwise the last open failure, or <c>MA_NO_BACKEND</c> if no context initialized.</returns>
    public static ma_result Open(ma_backend[] backends, ma_context* context, Func<nint, ma_result> open)
    {
        var result = ma_result.MA_NO_BACKEND;
        var config = Ma.ma_context_config_init();
        foreach (var backend in backends)
        {
            if (Ma.ma_is_backend_enabled(backend) == 0)
            {
                continue;
            }

            var candidate = backend;
            if (Ma.ma_context_init(&candidate, 1, &config, context) != ma_result.MA_SUCCESS)
            {
                continue;
            }

            result = open((nint)context);
            if (result == ma_result.MA_SUCCESS)
            {
                return result;
            }

            Ma.ma_context_uninit(context);
        }

        return result;
    }
}
