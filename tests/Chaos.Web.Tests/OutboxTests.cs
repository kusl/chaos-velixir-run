using Chaos.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chaos.Web.Tests;

public sealed class OutboxTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeliversEverythingInOrder()
    {
        var received = new List<int>();
        var gate = new Lock();
        await using var outbox = new Outbox<int>(1000, 16, TimeSpan.FromSeconds(5), (batch, _) =>
        {
            lock (gate)
            {
                received.AddRange(batch);
            }

            return Task.CompletedTask;
        }, NullLogger.Instance);

        for (var i = 0; i < 200; i++)
        {
            Assert.True(outbox.TryPost(i));
        }

        await Eventually(() =>
        {
            lock (gate)
            {
                return received.Count == 200;
            }
        });

        Assert.Equal(Enumerable.Range(0, 200), received);
    }

    [Fact]
    public async Task RefusesItemsWhenFull()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var outbox = new Outbox<int>(2, 1, TimeSpan.FromSeconds(30), async (_, _) =>
        {
            started.TrySetResult();
            await release.Task;
        }, NullLogger.Instance);

        Assert.True(outbox.TryPost(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        Assert.True(outbox.TryPost(2));
        Assert.True(outbox.TryPost(3));
        Assert.False(outbox.TryPost(4));
        release.TrySetResult();
    }

    [Fact]
    public async Task FaultsWhenSendingFails()
    {
        await using var outbox = new Outbox<int>(10, 1, TimeSpan.FromSeconds(5), (_, _) => throw new InvalidOperationException("gone"), NullLogger.Instance);
        Assert.True(outbox.TryPost(1));
        await Eventually(() => outbox.IsFaulted);
        Assert.False(outbox.TryPost(2));
    }

    [Fact]
    public async Task FaultsWhenTheClientIsTooSlow()
    {
        await using var outbox = new Outbox<int>(10, 1, TimeSpan.FromMilliseconds(50), (_, token) => Task.Delay(Timeout.Infinite, token), NullLogger.Instance);
        Assert.True(outbox.TryPost(1));
        await Eventually(() => outbox.IsFaulted);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(25, Cancellation);
        }

        Assert.True(condition());
    }
}
