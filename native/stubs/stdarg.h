/*
 * Minimal stdarg.h shim for ClangSharp code generation.
 * miniaudio.h's header section needs only the va_list type (for
 * ma_log_postv). The generator parses for the x86_64-pc-windows-msvc target,
 * where va_list is a plain char*. Not for compilation.
 */
#pragma once

typedef char* va_list;
