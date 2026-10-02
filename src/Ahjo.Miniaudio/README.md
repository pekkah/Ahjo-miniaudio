# Ahjo.Miniaudio

Idiomatic, low-allocation .NET wrapper for [miniaudio](https://github.com/mackron/miniaudio),
built for the Ahjo game engine. It sits on top of `Ahjo.Miniaudio.Native`, which
carries the raw bindings and the native binary.

> **Status:** in development. The API surface is not yet stable.

```csharp
using Ahjo.Miniaudio;

Console.WriteLine($"miniaudio {MiniaudioLibrary.Version}");
```

## Platform support

Follows `Ahjo.Miniaudio.Native`: `win-x64` today.

## Licensing

MIT. miniaudio itself is Public Domain / MIT No Attribution; its license text
ships in `Ahjo.Miniaudio.Native`.
