# Ahjo.Miniaudio.Native

Raw P/Invoke bindings for [miniaudio](https://github.com/mackron/miniaudio), with
the native runtime binary included for `win-x64`.

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

The shipped `ahjo_miniaudio.dll` is miniaudio compiled as a single translation unit
with its default feature set (all built-in decoders, all Windows backends loaded at
runtime). It links the MSVC runtime statically and imports nothing but `kernel32`,
so no Visual C++ redistributable is needed.

## Platform support

| RID | Shipped |
| --- | --- |
| `win-x64` | yes |
| everything else | not yet |

miniaudio's runtime-state structs (`ma_context`, `ma_device` and everything that
embeds them) have platform-specific layouts, and these bindings describe the
Windows x64 layout. Other RIDs ship only once their layout is proven by a CI lane.

## Licensing

miniaudio is dual-licensed Public Domain / MIT No Attribution (David Reid); its
license text ships in this package as `MINIAUDIO-LICENSE.txt`. This package's own
binding code is MIT.
