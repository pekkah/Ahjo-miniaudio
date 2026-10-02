cmake_minimum_required(VERSION 3.21)
project(ahjo_miniaudio LANGUAGES C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Link the MSVC runtime statically so the shipped DLL does not depend on the
# VC++ redistributable being installed on the consumer's machine. miniaudio
# loads every audio backend (WASAPI, DirectSound, WinMM) at runtime with
# LoadLibrary, so with a static CRT the DLL imports nothing but kernel32.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# Inputs configured by the MSBuild caller (see Ahjo.Miniaudio.Native.csproj):
#   AHJO_MINIAUDIO_INCLUDE_DIR — directory containing miniaudio.h. Defaults to
#                                the committed copy at include/.
#   AHJO_MINIAUDIO_DEFINES     — semicolon-separated preprocessor defines, from
#                                MiniaudioDefines in Directory.Build.props. The
#                                binding generator receives the same list; never
#                                add a struct-shaping define here directly.
if(NOT DEFINED AHJO_MINIAUDIO_INCLUDE_DIR)
    set(AHJO_MINIAUDIO_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/include")
endif()
if(NOT DEFINED AHJO_MINIAUDIO_DEFINES)
    set(AHJO_MINIAUDIO_DEFINES "")
endif()

add_library(ahjo_miniaudio SHARED src/ahjo_miniaudio.c)

target_include_directories(ahjo_miniaudio PRIVATE "${AHJO_MINIAUDIO_INCLUDE_DIR}")

# MA_DLL makes MA_API expand to __declspec(dllexport) on Windows and
# visibility("default") on GCC/Clang, so exactly miniaudio's public API is
# exported — no WINDOWS_EXPORT_ALL_SYMBOLS, no export list to maintain.
# It changes no struct layout, which is why it is not in MiniaudioDefines.
target_compile_definitions(ahjo_miniaudio PRIVATE MA_DLL ${AHJO_MINIAUDIO_DEFINES})

# CMake's platform defaults produce the names the C# side expects:
# ahjo_miniaudio.dll on Windows, libahjo_miniaudio.so on Linux,
# libahjo_miniaudio.dylib on macOS. Don't override PREFIX.
set_target_properties(ahjo_miniaudio PROPERTIES
    OUTPUT_NAME "ahjo_miniaudio"
    C_VISIBILITY_PRESET hidden
)

if(MSVC)
    # miniaudio is warning-clean at /W3 apart from a handful of intentional
    # patterns; don't let upstream diagnostics block our build.
    target_compile_options(ahjo_miniaudio PRIVATE /W3 /wd4100 /wd4127 /wd4244 /wd4267)
else()
    target_compile_options(ahjo_miniaudio PRIVATE -Wall -Wno-unused-parameter -Wno-unused-function -Wno-unused-variable)
endif()

if(UNIX AND NOT APPLE)
    # miniaudio dlopen()s ALSA/PulseAudio/JACK at runtime; it needs libdl,
    # pthreads and libm at link time.
    target_link_libraries(ahjo_miniaudio PRIVATE ${CMAKE_DL_LIBS} pthread m)
endif()

if(APPLE)
    set_target_properties(ahjo_miniaudio PROPERTIES
        BUILD_WITH_INSTALL_RPATH TRUE
        INSTALL_RPATH "@loader_path"
    )
endif()
