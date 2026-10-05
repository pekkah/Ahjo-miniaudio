# Ahjo.Miniaudio

C# bindings and a low-allocation wrapper for [miniaudio](https://github.com/mackron/miniaudio),
built for the Ahjo game engine. Targets .NET 11.

| Package | What it is | Status |
| --- | --- | --- |
| `Ahjo.Miniaudio.Native` | Raw P/Invoke bindings generated from `miniaudio.h` + the `ahjo_miniaudio` native binary | win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64 |
| `Ahjo.Miniaudio` | Idiomatic wrapper: device, decoder and engine tiers | preview, same RIDs |

## Building

Prerequisites: the .NET 11 SDK (pinned in `global.json`), cmake ≥ 3.21, and a C
toolchain (Visual Studio 2022+ with the C++ workload on Windows; gcc or clang on
Linux; the Xcode command-line tools on macOS).

```bash
dotnet tool restore
dotnet build Ahjo.Miniaudio.slnx   # builds ahjo_miniaudio for the host via cmake, then the managed projects
dotnet test
```

## Sample

`samples/HelloAudio` shows the wrapper in use: engine setup, a generated tone
driven from a game-style loop (pan + pitch), streaming a WAV/FLAC/MP3 file, and
an engine pulled through a caller-owned `AudioDevice` render callback.

```bash
dotnet run --project samples/HelloAudio                  # 440 Hz tone, panning left -> right
dotnet run --project samples/HelloAudio -- music.flac    # stream a file to the end
dotnet run --project samples/HelloAudio -- --null        # no speakers: miniaudio's null backend
dotnet run --project samples/HelloAudio -- --pull        # the engine mixed from an AudioDevice callback
```

It also serves as the Native AOT check:
`dotnet publish samples/HelloAudio -c Release -r <rid>` (e.g. `win-x64`, `linux-x64`,
`osx-arm64`; Native AOT publishes for the host OS only).

## Regenerating the bindings

The miniaudio version is pinned by `MiniaudioVersion` in `Directory.Build.props`.
Both `native/miniaudio/include/miniaudio.h` and the generated C# under
`src/Ahjo.Miniaudio.Native/Generated/` are committed, so a normal build needs no
network access. To move the pin:

```bash
# edit MiniaudioVersion in Directory.Build.props, then:
dotnet build src/Ahjo.Miniaudio.Native -t:Regenerate
dotnet build Ahjo.Miniaudio.slnx && dotnet test
```

## Releasing

`.github/workflows/publish.yml` publishes a preview package on every push to `main`
and a stable one when a GitHub Release is published on a `v*` tag (versions come
from [MinVer](https://github.com/adamralph/minver)). Pushing to NuGet.org is off
until the `NUGET_PUBLISH` repository variable is `true` **and** the `NUGET_USER`
variable names the nuget.org profile that owns the packages; until then packages
are only uploaded as workflow artifacts. There is no API key: the push uses
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing),
so that profile needs a policy on nuget.org for this repository and `publish.yml`.

## License

MIT for this repository's code. miniaudio itself is Public Domain / MIT-0; see
`native/miniaudio/MINIAUDIO-LICENSE.txt`.
