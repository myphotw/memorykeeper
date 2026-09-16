using MemoryKeeper.App.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class SingleInstanceEventSubscriptionTests
{
    [Fact]
    public void Attach_RetriesAfterMissingSource_AndAvoidsDuplicateOrStaleSubscriptions()
    {
        var subscription = new SingleInstanceEventSubscription<EventSource>();
        var first = new EventSource();
        var second = new EventSource();
        var handled = 0;
        EventHandler handler = (_, _) => handled++;

        Assert.False(subscription.Attach(
            null,
            source => source.Changed += handler,
            source => source.Changed -= handler));

        Assert.True(subscription.Attach(
            first,
            source => source.Changed += handler,
            source => source.Changed -= handler));
        first.RaiseChanged();
        Assert.Equal(1, handled);

        Assert.True(subscription.Attach(
            first,
            source => source.Changed += handler,
            source => source.Changed -= handler));
        first.RaiseChanged();
        Assert.Equal(2, handled);

        Assert.True(subscription.Attach(
            second,
            source => source.Changed += handler,
            source => source.Changed -= handler));
        first.RaiseChanged();
        Assert.Equal(2, handled);
        second.RaiseChanged();
        Assert.Equal(3, handled);

        subscription.Detach(source => source.Changed -= handler);
        second.RaiseChanged();
        Assert.Equal(3, handled);
        Assert.Null(subscription.Current);
    }

    private sealed class EventSource
    {
        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
