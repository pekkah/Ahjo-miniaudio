/*
 * Minimal stdalign.h shim for ClangSharp code generation on a POSIX target.
 * miniaudio.h includes it for MA_ATOMIC, which spells the C11 keyword
 * _Alignas directly, so nothing needs defining here. Not for compilation.
 */
#pragma once
