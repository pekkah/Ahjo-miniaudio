---
name: regen-bindings
description: Regenerate the miniaudio ClangSharp bindings safely — version pin bump, regen target, diff sanity check, build + tests. Use when the user asks to "regen bindings", "bump miniaudio", or when tools/generate-miniaudio.rsp or MiniaudioDefines changes.
---

# regen-bindings

`src/Ahjo.Miniaudio.Native/Generated/` and `native/miniaudio/include/miniaudio.h` are generator output and fetched input — never hand-edit them. The editable inputs are `tools/generate-miniaudio.rsp` and, in `Directory.Build.props`, `MiniaudioVersion` and `MiniaudioDefines`.

## Procedure

1. **Edit the input**, not the output. A `MiniaudioVersion` bump is release-visible; confirm the user wants it if they only asked vaguely. Pins are release tags (see CLAUDE.md, "Version pin").
2. **Regenerate:** `dotnet build src/Ahjo.Miniaudio.Native -t:Regenerate` (needs network; refetches the tag, restages `miniaudio.h` and the license, wipes and rewrites `Generated/`).
3. **Sanity-check the diff:** `git diff --stat`. Read the `miniaudio.h` diff first — it is the API change; the `Generated/` churn should be a mechanical reflection of it. Red flags: whole files disappearing, `Generated/` changing when only the rsp comment changed, a tiny diff after a large upstream changelog.
4. **Check portability:** `tools/check-bindings-portable.sh` (bash; Git Bash on Windows). It regenerates for `x86_64-pc-linux-gnu`, `aarch64-linux-gnu` and `arm64-apple-macos` and diffs against `Generated/` after normalizing the two expected, layout-neutral differences (enum base type, `char` signedness). Any remaining diff is a new platform dependency — usually a struct that embeds an opaque type (`ma_mutex`, `ma_context`, …) by value, or a new `*_w` entry point. Extend the rsp's `--exclude` / `--remap` lists (plus `Manual/Opaque.cs` and an `AHJO_MA_SIZEOF` line for a newly opaque struct) and regenerate; never ship the Windows-only shape.
5. **Build + test:** `dotnet build Ahjo.Miniaudio.slnx && dotnet test`. The build rebuilds `ahjo_miniaudio` (incremental on the header). `VersionMatchesPinnedRelease` must pass, and **every `LayoutTests` case must pass** — a mismatch means the rsp/defines disagree with the compile, not that the test is wrong.
6. **Commit** pin + rsp + header + generated output together: `Native: bump miniaudio to X.Y.Z`. Don't mix with wrapper changes.
