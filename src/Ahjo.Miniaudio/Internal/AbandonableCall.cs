using System.Runtime.ExceptionServices;

using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio.Internal;

/// <summary>
/// Runs a native init call that waits on the backend for as long as the
/// backend takes. miniaudio's PulseAudio waits have no timeout: against a
/// server that accepts the connection and never answers, <c>ma_context_init</c>
/// blocks forever, and nothing outside that loop can wake it.
/// </summary>
/// <remarks>
/// <para>A token that cannot be cancelled runs the call inline, exactly as
/// before. A cancellable one runs it on a dedicated background thread (not the
/// pool: a call that never returns would park a pool thread for good) while
/// the caller waits on the token.</para>
/// <para>Cancelling stops the <i>caller</i> waiting; the call itself cannot be
/// interrupted. From then on the thread owns everything the call uses, and
/// passes the late result to <c>abandon</c>, which must release it: uninit
/// only on <c>MA_SUCCESS</c>, then free (a token already cancelled on entry
/// calls it with <c>MA_CANCELLED</c> without running the call). So the call
/// must read nothing from the caller's
/// stack — build its configs inside it. A call that never returns keeps its
/// thread and its memory until the process exits.</para>
/// <para>The objects the call runs against are its owners: each one's
/// <see cref="InFlightCalls"/> keeps its native state alive until the call
/// returns, even if it is disposed meanwhile. <c>inner</c> is the object
/// itself (a device), <c>outer</c> what it depends on (its context); they are
/// ended inner first, so a deferred device uninit still has its context.</para>
/// </remarks>
internal static class AbandonableCall
{
    public static ma_result Run(
        Func<ma_result> call,
        Action<ma_result> abandon,
        CancellationToken cancellationToken,
        InFlightCalls? inner = null,
        InFlightCalls? outer = null)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return call();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // Never ran, so there is nothing to uninit; abandon frees.
            abandon(ma_result.MA_CANCELLED);
            cancellationToken.ThrowIfCancellationRequested();
        }

        inner?.Begin();
        try
        {
            outer?.Begin();
        }
        catch
        {
            inner?.End();
            throw;
        }

        var pending = new Pending(call, abandon, inner, outer);
        var thread = new Thread(pending.Execute)
        {
            IsBackground = true,
            Name = "Ahjo.Miniaudio init",
        };
        thread.UnsafeStart();
        return pending.Wait(cancellationToken);
    }

    private sealed class Pending(Func<ma_result> call, Action<ma_result> abandon, InFlightCalls? inner, InFlightCalls? outer)
    {
        private readonly Lock _lock = new();
        private readonly ManualResetEventSlim _done = new();
        private ma_result _result = ma_result.MA_ERROR;
        private ExceptionDispatchInfo? _exception;
        private bool _completed;
        private bool _abandoned;

        public void Execute()
        {
            var result = ma_result.MA_ERROR;
            ExceptionDispatchInfo? exception = null;
            try
            {
                result = call();
            }
            catch (Exception e)
            {
                // An exception escaping a background thread ends the process.
                // Hand it to the caller, or treat it as a failed init (nothing
                // to uninit) if the caller has gone.
                exception = ExceptionDispatchInfo.Capture(e);
            }

            bool abandoned;
            lock (_lock)
            {
                _completed = true;
                _result = result;
                _exception = exception;
                abandoned = _abandoned;
                if (!abandoned)
                {
                    _done.Set();
                }
            }

            if (abandoned)
            {
                try
                {
                    abandon(exception is null ? result : ma_result.MA_ERROR);
                }
                finally
                {
                    _done.Dispose();
                    EndOwners();
                }
            }
        }

        public ma_result Wait(CancellationToken cancellationToken)
        {
            try
            {
                _done.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                bool completed;
                lock (_lock)
                {
                    completed = _completed;
                    _abandoned = !completed;
                }

                if (!completed)
                {
                    throw; // Execute owns the cleanup now.
                }

                // The call finished as the token fired: its result stands.
            }

            _done.Dispose();
            EndOwners();
            _exception?.Throw();
            return _result;
        }

        private void EndOwners()
        {
            try
            {
                inner?.End();
            }
            finally
            {
                outer?.End();
            }
        }
    }
}
