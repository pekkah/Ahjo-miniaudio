# Ahjo.Miniaudio

C# bindings and a low-allocation wrapper for [miniaudio](https://github.com/mackron/miniaudio),
built for the Ahjo game engine. Targets .NET 11.

| Package | What it is | Status |
| --- | --- | --- |
| `Ahjo.Miniaudio.Native` | Raw P/Invoke bindings generated from `miniaudio.h` + the `ahjo_miniaudio` native binary | win-x64 |
| `Ahjo.Miniaudio` | Idiomatic wrapper | in development, not published |

## Building

Prerequisites: the .NET 11 SDK (pinned in `global.json`), cmake ≥ 3.21, and a C
toolchain (Visual Studio 2022+ with the C++ workload on Windows).

```bash
dotnet tool restore
dotnet build Ahjo.Miniaudio.slnx   # builds ahjo_miniaudio for the host via cmake, then the managed projects
dotnet test
```

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
until the `NUGET_PUBLISH` repository variable is `true` **and** the `NUGET_KEY`
secret is set; until then packages are only uploaded as workflow artifacts.

## License

MIT for this repository's code. miniaudio itself is Public Domain / MIT-0; see
`native/miniaudio/MINIAUDIO-LICENSE.txt`.
