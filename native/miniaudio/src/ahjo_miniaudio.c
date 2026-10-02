/*
 * The single translation unit of the ahjo_miniaudio shared library.
 *
 * miniaudio is a single-header library; this file is where its
 * implementation is compiled. MA_DLL (set by CMakeLists.txt) turns MA_API
 * into an export attribute, so the library exports exactly miniaudio's
 * public API plus the ahjo_ma_* functions below.
 */
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

/*
 * Layout oracle. Each function returns the compiler's sizeof for one public
 * miniaudio type, so Ahjo.Miniaudio.Native.Tests can compare it against the
 * generated C# struct on the RID the binary was built for. A mismatch means
 * the bindings describe a different struct than the one the library writes
 * to — the failure mode that otherwise surfaces only as heap corruption.
 *
 * Not part of the generated bindings: tools/generate-miniaudio.rsp parses
 * miniaudio.h only. The tests declare these imports themselves.
 */
#define AHJO_MA_SIZEOF(T) MA_API size_t ahjo_ma_sizeof_##T(void) { return sizeof(T); }

AHJO_MA_SIZEOF(ma_context)
AHJO_MA_SIZEOF(ma_context_config)
AHJO_MA_SIZEOF(ma_device)
AHJO_MA_SIZEOF(ma_device_config)
AHJO_MA_SIZEOF(ma_device_info)
AHJO_MA_SIZEOF(ma_decoder)
AHJO_MA_SIZEOF(ma_decoder_config)
AHJO_MA_SIZEOF(ma_encoder)
AHJO_MA_SIZEOF(ma_encoder_config)
AHJO_MA_SIZEOF(ma_engine)
AHJO_MA_SIZEOF(ma_engine_config)
AHJO_MA_SIZEOF(ma_sound)
AHJO_MA_SIZEOF(ma_sound_config)
AHJO_MA_SIZEOF(ma_sound_group)
AHJO_MA_SIZEOF(ma_resource_manager)
AHJO_MA_SIZEOF(ma_resource_manager_config)
AHJO_MA_SIZEOF(ma_node_graph)
AHJO_MA_SIZEOF(ma_mutex)
AHJO_MA_SIZEOF(ma_event)
AHJO_MA_SIZEOF(ma_semaphore)
AHJO_MA_SIZEOF(ma_thread)
