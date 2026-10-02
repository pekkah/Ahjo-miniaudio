/*
 * Minimal stddef.h shim for ClangSharp code generation.
 * Provides just enough so libclang can parse miniaudio.h without a
 * system C toolchain being available. Not for compilation.
 */
#pragma once

typedef unsigned long long size_t;
typedef long long          ptrdiff_t;

#ifndef NULL
#define NULL ((void*)0)
#endif

#define offsetof(s, m) ((size_t)&(((s*)0)->m))
