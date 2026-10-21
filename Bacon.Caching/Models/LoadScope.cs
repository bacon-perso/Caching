namespace Bacon.Caching.Models;

internal sealed class LoadScope : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Lock _gate = new();
    private int _waiters = 1;   // the caller that starts the load is the first waiter
    private bool _isDisposed;

    public CancellationToken Token => _cancellation.Token;

    // Joins only a load that still has waiters. Once the count reaches 0 it is abandoned and never revived.
    public bool TryJoin()
    {
        int current = Volatile.Read(ref _waiters);

        while (current > 0)
        {
            int seen = Interlocked.CompareExchange(ref _waiters, current + 1, current);

            if (seen == current)
            {
                return true;
            }

            current = seen;
        }

        return false;
    }

    // The last waiter to leave an unfinished load cancels it.
    public void Leave()
    {
        if (Interlocked.Decrement(ref _waiters) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (AggregateException)
            {
                // A cancellation callback registered by the factory threw. It must not surface in the caller that happened to leave last.
            }
        }
    }

    // Called by the load when it ends, whatever the outcome. Safe to call more than once.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _cancellation.Dispose();
        }
    }
}