using DXNexus.Bridge.Core;
using Xunit;

namespace DXNexus.Bridge.Core.Tests;

public sealed class OfflineMutationQueueTests
{
    [Fact]
    public async Task QueueIsIdempotentDurableAndRemovable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdrnexus-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "queue.sqlite3");
        var id = Guid.CreateVersion7();
        try
        {
            await using (var queue = new OfflineMutationQueue(path))
            {
                await queue.EnqueueAsync(id, "logbook", "{\"test\":true}");
                await queue.EnqueueAsync(id, "logbook", "{\"test\":false}");
                var due = await queue.DueAsync();
                var item = Assert.Single(due);
                Assert.Equal(id, item.Id);
                Assert.Equal("{\"test\":true}", item.PayloadJson);
            }

            await using (var reopened = new OfflineMutationQueue(path))
            {
                Assert.Single(await reopened.DueAsync());
                await reopened.CompleteAsync(id);
                Assert.Empty(await reopened.DueAsync());
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RetryMovesMutationOutOfTheImmediateDueWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdrnexus-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "queue.sqlite3");
        var id = Guid.CreateVersion7();
        try
        {
            await using var queue = new OfflineMutationQueue(path);
            await queue.EnqueueAsync(id, "wishlist", "{}");
            await queue.RetryLaterAsync(id, 0);
            Assert.Empty(await queue.DueAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
