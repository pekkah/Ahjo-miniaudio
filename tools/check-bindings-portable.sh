#!/usr/bin/env bash
# Regenerates the bindings for POSIX targets into a temp dir and diffs them
# against the committed (x86_64-pc-windows-msvc) output. They must match:
# one set of bindings serves every RID (CLAUDE.md, "Platform layouts").
#
# Two differences are expected and normalized away, since neither changes a
# layout: enum base type (int on MSVC, uint elsewhere) and char signedness
# (sbyte vs byte on aarch64-linux). Anything else is a platform dependency
# the rsp's --exclude / --remap lists no longer cover; typically a new
# upstream struct embedding an opaque type by value, or a new *_w function.
#
# Usage, from the repo root after `dotnet tool restore`:
#   tools/check-bindings-portable.sh
set -euo pipefail

out="${TMPDIR:-${TEMP:-/tmp}}/ahjo-miniaudio-bindings-portable"
rm -rf "$out"
mkdir -p "$out"
# The generator is a .NET tool; on Windows it needs a native path.
out_native="$(cygpath -m "$out" 2>/dev/null || echo "$out")"

normalize() {
    sed -i \
        -e 's/\bsbyte\b/byte/g' \
        -e '/NativeTypeName("unsigned int")/d' \
        -e 's/^\(public enum [A-Za-z0-9_]*\) : uint$/\1/' \
        "$1"/*.cs
}

cp -r src/Ahjo.Miniaudio.Native/Generated "$out/committed"
normalize "$out/committed"

status=0
for target in x86_64-pc-linux-gnu aarch64-linux-gnu arm64-apple-macos; do
    sed -e "s#^src/Ahjo.Miniaudio.Native/Generated\r\?\$#$out_native/$target#" \
        -e "s#^--target=.*#--target=$target#" \
        tools/generate-miniaudio.rsp > "$out/$target.rsp"
    dotnet tool run ClangSharpPInvokeGenerator @"$out_native/$target.rsp" > /dev/null
    normalize "$out/$target"
    if diff -r "$out/committed" "$out/$target"; then
        echo "$target: matches"
    else
        echo "$target: DIFFERS from the committed bindings (above)" >&2
        status=1
    fi
done
exit $status
