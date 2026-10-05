/*
 * Minimal stdarg.h shim for ClangSharp code generation.
 * miniaudio.h's header section needs only the va_list type (for
 * ma_log_postv). clang's builtin is the right type for whichever target the
 * header is parsed for. Not for compilation.
 */
#pragma once

typedef __builtin_va_list va_list;
