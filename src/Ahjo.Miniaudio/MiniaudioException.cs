using System.Runtime.InteropServices;

using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>
/// A miniaudio call returned something other than <c>MA_SUCCESS</c>.
/// </summary>
/// <remarks>
/// Raised only for failures the native library reports. Misuse of the wrapper
/// (a bad description, a disposed object, the wrong mode) raises the usual
/// <see cref="ArgumentException"/> / <see cref="ObjectDisposedException"/> /
/// <see cref="InvalidOperationException"/> instead, so a consumer that turns
/// native failures into result values can catch exactly this type and read
/// <see cref="Result"/>.
/// </remarks>
public sealed class MiniaudioException : Exception
{
    /// <summary>Creates the exception for <paramref name="operation"/> failing with <paramref name="result"/>.</summary>
    public MiniaudioException(ma_result result, string operation)
        : base($"{operation} failed: {Describe(result)} ({result})")
    {
        Result = result;
        Operation = operation;
    }

    /// <summary>Creates the exception for <paramref name="operation"/> failing with <paramref name="result"/>, with advice on what to do about it.</summary>
    public MiniaudioException(ma_result result, string operation, string detail)
        : base($"{operation} failed: {Describe(result)} ({result}). {detail}")
    {
        Result = result;
        Operation = operation;
    }

    /// <summary>The raw result the native call returned.</summary>
    public ma_result Result { get; }

    /// <summary>The miniaudio function (or wrapper step) that failed.</summary>
    public string Operation { get; }

    private static unsafe string Describe(ma_result result) =>
        Marshal.PtrToStringUTF8((nint)Ma.ma_result_description(result)) ?? "unknown error";
}
