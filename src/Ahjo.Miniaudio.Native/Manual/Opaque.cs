namespace Ahjo.Miniaudio.Native;

// miniaudio's runtime-state types, whose layout differs per platform:
// per-backend members under MA_SUPPORT_*, and HANDLEs on Windows versus
// pthread types on POSIX. tools/generate-miniaudio.rsp excludes them, so the
// one set of bindings fits every RID, and they are declared here as opaque.
//
// Use them only through pointers. sizeof(T) of an empty C# struct is 1, not
// the native size: allocate Ma.ahjo_ma_sizeof_<T>() bytes instead, and read
// fields through an ahjo_ma_* accessor (native/miniaudio/src/ahjo_miniaudio.c)
// or a miniaudio getter, never by declaring them here.

/// <summary>Opaque <c>ma_context</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_context"/> bytes.</summary>
public partial struct ma_context;

/// <summary>Opaque <c>ma_device</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_device"/> bytes.</summary>
public partial struct ma_device;

/// <summary>Opaque <c>ma_resource_manager</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_resource_manager"/> bytes.</summary>
public partial struct ma_resource_manager;

/// <summary>Opaque <c>ma_log</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_log"/> bytes.</summary>
public partial struct ma_log;

/// <summary>Opaque <c>ma_fence</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_fence"/> bytes.</summary>
public partial struct ma_fence;

/// <summary>Opaque <c>ma_async_notification_event</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_async_notification_event"/> bytes.</summary>
public partial struct ma_async_notification_event;

/// <summary>Opaque <c>ma_job_queue</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_job_queue"/> bytes.</summary>
public partial struct ma_job_queue;

/// <summary>Opaque <c>ma_device_job_thread</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_device_job_thread"/> bytes.</summary>
public partial struct ma_device_job_thread;

/// <summary>Opaque <c>ma_mutex</c> (a HANDLE on Windows, a pthread mutex on POSIX). Allocate <see cref="Ma.ahjo_ma_sizeof_ma_mutex"/> bytes.</summary>
public partial struct ma_mutex;

/// <summary>Opaque <c>ma_event</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_event"/> bytes.</summary>
public partial struct ma_event;

/// <summary>Opaque <c>ma_semaphore</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_semaphore"/> bytes.</summary>
public partial struct ma_semaphore;

/// <summary>Opaque <c>ma_thread</c>. Allocate <see cref="Ma.ahjo_ma_sizeof_ma_thread"/> bytes.</summary>
public partial struct ma_thread;
