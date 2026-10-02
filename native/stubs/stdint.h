/*
 * Minimal stdint.h shim for ClangSharp code generation.
 * Provides the fixed-width integer typedefs miniaudio.h references at
 * parse time. Not for compilation — libclang only needs these to
 * satisfy preprocessor + type lookup when no system C toolchain is
 * available on the host.
 */
#pragma once

typedef signed char        int8_t;
typedef short              int16_t;
typedef int                int32_t;
typedef long long          int64_t;

typedef unsigned char      uint8_t;
typedef unsigned short     uint16_t;
typedef unsigned int       uint32_t;
typedef unsigned long long uint64_t;

typedef long long          intptr_t;
typedef unsigned long long uintptr_t;

#define INT8_MIN   (-128)
#define INT16_MIN  (-32767-1)
#define INT32_MIN  (-2147483647-1)
#define INT64_MIN  (-9223372036854775807LL-1)
#define INT8_MAX   127
#define INT16_MAX  32767
#define INT32_MAX  2147483647
#define INT64_MAX  9223372036854775807LL
#define UINT8_MAX  0xff
#define UINT16_MAX 0xffff
#define UINT32_MAX 0xffffffffu
#define UINT64_MAX 0xffffffffffffffffull
