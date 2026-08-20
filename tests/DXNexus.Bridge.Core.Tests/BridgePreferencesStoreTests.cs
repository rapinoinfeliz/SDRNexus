using DXNexus.Bridge.Core;
using Xunit;

namespace DXNexus.Bridge.Core.Tests;

public sealed class BridgePreferencesStoreTests
{
    [Fact]
    public async Task LoadsLegacySetupAndPersistsLiveCompanionChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdrnexus-prefs-{Guid.NewGuid():N}.json");
        var pointId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        try
        {
            await File.WriteAllTextAsync(path,
                $$"""{"listeningPointId":"{{pointId}}","receiverProfileId":"{{receiverId}}"}""");
            var store = new BridgePreferencesStore(path);
            var legacy = await store.LoadAsync();
            Assert.Equal(pointId, legacy.ListeningPointId);
            Assert.Equal(receiverId, legacy.ReceiverProfileId);
            Assert.False(legacy.LiveBrowserCompanion);

            await store.SaveAsync(legacy with { LiveBrowserCompanion = true });
            Assert.True((await store.LoadAsync()).LiveBrowserCompanion);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
