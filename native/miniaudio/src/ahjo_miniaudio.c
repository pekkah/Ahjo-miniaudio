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
 * miniaudio.h only. The exports for the opaque types are declared in
 * src/Ahjo.Miniaudio.Native/Manual/ (they are how those types get
 * allocated); the tests look the rest up by name.
 */
#define AHJO_MA_SIZEOF(T) MA_API size_t ahjo_ma_sizeof_##T(void) { return sizeof(T); }

/* Generated types: compared against the C# struct by LayoutTests. */
AHJO_MA_SIZEOF(ma_context_config)
AHJO_MA_SIZEOF(ma_device_id)
AHJO_MA_SIZEOF(ma_device_config)
AHJO_MA_SIZEOF(ma_device_info)
AHJO_MA_SIZEOF(ma_device_notification)
AHJO_MA_SIZEOF(ma_decoder)
AHJO_MA_SIZEOF(ma_decoder_config)
AHJO_MA_SIZEOF(ma_audio_buffer_ref)
AHJO_MA_SIZEOF(ma_encoder)
AHJO_MA_SIZEOF(ma_encoder_config)
AHJO_MA_SIZEOF(ma_engine)
AHJO_MA_SIZEOF(ma_engine_config)
AHJO_MA_SIZEOF(ma_sound)
AHJO_MA_SIZEOF(ma_sound_config)
AHJO_MA_SIZEOF(ma_sound_group)
AHJO_MA_SIZEOF(ma_resource_manager_config)
AHJO_MA_SIZEOF(ma_node_graph)
AHJO_MA_SIZEOF(ma_vec3f)

/*
 * Opaque types: their layout differs per platform, so the bindings declare
 * them as empty structs (Manual/Opaque.cs) and allocate this many bytes.
 */
AHJO_MA_SIZEOF(ma_context)
AHJO_MA_SIZEOF(ma_device)
AHJO_MA_SIZEOF(ma_resource_manager)
AHJO_MA_SIZEOF(ma_log)
AHJO_MA_SIZEOF(ma_fence)
AHJO_MA_SIZEOF(ma_async_notification_event)
AHJO_MA_SIZEOF(ma_job_queue)
AHJO_MA_SIZEOF(ma_device_job_thread)
AHJO_MA_SIZEOF(ma_mutex)
AHJO_MA_SIZEOF(ma_event)
AHJO_MA_SIZEOF(ma_semaphore)
AHJO_MA_SIZEOF(ma_thread)

/*
 * Field offsets, for the fields of generated (portable) structs that the
 * wrapper reads or writes directly. sizeof alone cannot catch a field that
 * moved inside a struct of unchanged size; LayoutTests compares these
 * against the C# field offsets.
 */
#define AHJO_MA_OFFSETOF(T, F) MA_API size_t ahjo_ma_offsetof_##T##_##F(void) { return offsetof(T, F); }

AHJO_MA_OFFSETOF(ma_engine, pProcessUserData)
AHJO_MA_OFFSETOF(ma_device_notification, pDevice)
AHJO_MA_OFFSETOF(ma_device_notification, type)
AHJO_MA_OFFSETOF(ma_device_info, id)
AHJO_MA_OFFSETOF(ma_device_info, name)
AHJO_MA_OFFSETOF(ma_device_info, isDefault)
AHJO_MA_OFFSETOF(ma_audio_buffer_ref, sampleRate)

/*
 * Accessors for the opaque types' fields the wrapper needs and miniaudio
 * has no getter for. Each is a plain load: no locking, no callbacks, so the
 * managed side may call them with [SuppressGCTransition].
 */
MA_API ma_backend ahjo_ma_context_get_backend(const ma_context* pContext)
{
    return pContext->backend;
}

MA_API void* ahjo_ma_device_get_user_data(const ma_device* pDevice)
{
    return pDevice->pUserData;
}

MA_API ma_uint32 ahjo_ma_device_get_playback_channels(const ma_device* pDevice)
{
    return pDevice->playback.channels;
}

MA_API ma_uint32 ahjo_ma_device_get_sample_rate(const ma_device* pDevice)
{
    return pDevice->sampleRate;
}
