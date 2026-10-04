# Ahjo.Miniaudio.Native

Raw P/Invoke bindings for [miniaudio](https://github.com/mackron/miniaudio), with
the native runtime binary included for Windows, Linux and macOS.

The managed surface is generated with
[ClangSharpPInvokeGenerator](https://github.com/dotnet/ClangSharp) from
`miniaudio.h` at a pinned upstream release tag, and is 1:1 with the header:
functions live on the static class `Ma`, types and enum members keep their C names.

```csharp
using System.Runtime.InteropServices;
using Ahjo.Miniaudio.Native;

unsafe
{
    var engine = (ma_engine*)NativeMemory.AlignedAlloc((nuint)sizeof(ma_engine), 64);
    if (Ma.ma_engine_init(null, engine) != ma_result.MA_SUCCESS) { /* handle */ }

    fixed (byte* path = "explosion.wav\0"u8)
    {
        Ma.ma_engine_play_sound(engine, (sbyte*)path, null);
    }

    // ... later
    Ma.ma_engine_uninit(engine);
    NativeMemory.AlignedFree(engine);
}
```

## The native library

The shipped `ahjo_miniaudio` library is miniaudio compiled as a single translation
unit with its default feature set: all built-in decoders, and every backend of the
platform loaded at runtime (WASAPI/DirectSound/WinMM, PulseAudio/ALSA/JACK,
Core Audio). On Windows it links the MSVC runtime statically and imports nothing
but `kernel32`, so no Visual C++ redistributable is needed. The Linux build needs
glibc 2.35 or newer (Ubuntu 22.04+); the macOS build needs macOS 12.0 or newer.

## Platform support

| RID | Shipped |
| --- | --- |
| `win-x64`, `win-arm64` | yes |
| `linux-x64`, `linux-arm64` | yes |
| `osx-arm64` | yes |
| everything else | not yet |

One managed assembly serves every RID. miniaudio's runtime-state types have
platform-specific layouts, so they are **opaque** here: `ma_context`, `ma_device`,
`ma_resource_manager`, `ma_log`, `ma_fence`, `ma_async_notification_event`,
`ma_job_queue`, `ma_device_job_thread`, `ma_mutex`, `ma_event`, `ma_semaphore`
and `ma_thread` have no fields, and `sizeof` of one is meaningless. Allocate the
size the binary reports, and read the fields you need through miniaudio's getters
or the `ahjo_ma_*` accessors:

```csharp
var device = (ma_device*)NativeMemory.AlignedAlloc(Ma.ahjo_ma_sizeof_ma_device(), 64);
// ... ma_device_init(...)
var channels = Ma.ahjo_ma_device_get_playback_channels(device);
```

The `*_w` (wide-string) functions and `ma_log_postv` are not bound: `wchar_t` and
`va_list` differ per platform. Use the UTF-8 variants.

## Licensing

miniaudio is dual-licensed Public Domain / MIT No Attribution (David Reid); its
license text ships in this package as `MINIAUDIO-LICENSE.txt`. This package's own
binding code is MIT.
