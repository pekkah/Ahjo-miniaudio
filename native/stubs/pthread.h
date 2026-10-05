/*
 * Minimal pthread.h shim for ClangSharp code generation on a POSIX target.
 * miniaudio.h includes pthread.h in its header section for the types behind
 * ma_thread / ma_mutex / ma_event / ma_semaphore. Every struct that embeds
 * them is excluded from generation (tools/generate-miniaudio.rsp) and is
 * opaque on the C# side, so these sizes reach no generated layout; they are
 * dummies, only big enough to look plausible. Not for compilation.
 *
 * Deliberately not MA_NO_PTHREAD_IN_HEADER: that define changes miniaudio's
 * struct layouts, so it could only go through MiniaudioDefines (CLAUDE.md,
 * invariant 2), where a parse-time convenience does not belong.
 */
#pragma once

typedef unsigned long pthread_t;
typedef union { char __size[64]; long long __align; } pthread_mutex_t;
typedef union { char __size[64]; long long __align; } pthread_cond_t;
