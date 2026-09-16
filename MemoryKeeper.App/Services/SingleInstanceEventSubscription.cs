namespace MemoryKeeper.App.Services;

/// <summary>
/// Owns one event source at a time and switches subscriptions without duplicates.
/// </summary>
internal sealed class SingleInstanceEventSubscription<T> where T : class
{
    public T? Current { get; private set; }

    public bool Attach(T? candidate, Action<T> subscribe, Action<T> unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        if (candidate is null)
        {
            return false;
        }

        if (ReferenceEquals(Current, candidate))
        {
            return true;
        }

        if (Current is not null)
        {
            unsubscribe(Current);
            Current = null;
        }

        subscribe(candidate);
        Current = candidate;
        return true;
    }

    public void Detach(Action<T> unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(unsubscribe);
        if (Current is null)
        {
            return;
        }

        unsubscribe(Current);
        Current = null;
    }
}
