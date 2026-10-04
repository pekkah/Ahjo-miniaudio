/*
 * Minimal stddef.h shim for ClangSharp code generation.
 * Provides just enough so libclang can parse miniaudio.h without a
 * system C toolchain being available. Not for compilation.
 */
#pragma once

typedef unsigned long long size_t;
typedef long long          ptrdiff_t;

/* MSVC targets get wchar_t from miniaudio.h itself (a 16-bit typedef); on a
   POSIX target it comes from here. Only the *_w entry points and pFilePathW
   use it, and those are pointers. */
#if !defined(_MSC_VER)
typedef __WCHAR_TYPE__     wchar_t;
#endif

#ifndef NULL
#define NULL ((void*)0)
#endif

#define offsetof(s, m) ((size_t)&(((s*)0)->m))
