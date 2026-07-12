using Fullview.Api.Sync;
using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Api.Tests.Sync;

public class SyncServiceTests
{
    private static Todo MakeTodo(string id, DateTimeOffset updatedAt, string updatedBy, string title, bool deleted = false) => new()
    {
        Id = id,
        Context = SyncContext.Personal,
        UpdatedAt = updatedAt,
        UpdatedBy = updatedBy,
        Title = title,
        Deleted = deleted
    };

    [Fact]
    public async Task Newer_offline_edit_beats_an_older_stored_web_edit()
    {
        var store = new InMemorySyncStore();
        var service = new SyncService(store);
        var t0 = DateTimeOffset.Parse("2026-07-11T08:00:00Z");

        // Web wrote first (older).
        await service.ApplyMutationAsync(MakeTodo("todo-1", t0, "web", "Original title"), CancellationToken.None);

        // Device syncs an edit made offline, timestamped later.
        await service.ApplyMutationAsync(MakeTodo("todo-1", t0.AddMinutes(5), "device-1", "Edited offline"), CancellationToken.None);

        var stored = await store.GetAsync("Todo#todo-1", CancellationToken.None);
        Assert.Equal("Edited offline", Assert.IsType<Todo>(stored).Title);
    }

    [Fact]
    public async Task Older_web_edit_arriving_after_a_newer_device_edit_is_discarded()
    {
        var store = new InMemorySyncStore();
        var service = new SyncService(store);
        var t0 = DateTimeOffset.Parse("2026-07-11T08:00:00Z");

        await service.ApplyMutationAsync(MakeTodo("todo-1", t0.AddMinutes(5), "device-1", "Newer device edit"), CancellationToken.None);

        // A stale web mutation (older timestamp) arrives after the fact — must not win.
        await service.ApplyMutationAsync(MakeTodo("todo-1", t0, "web", "Stale web edit"), CancellationToken.None);

        var stored = await store.GetAsync("Todo#todo-1", CancellationToken.None);
        Assert.Equal("Newer device edit", Assert.IsType<Todo>(stored).Title);
    }

    [Fact]
    public async Task Replaying_the_same_mutation_is_idempotent()
    {
        var store = new InMemorySyncStore();
        var service = new SyncService(store);
        var mutation = MakeTodo("todo-1", DateTimeOffset.Parse("2026-07-11T08:00:00Z"), "device-1", "Book Yael gym session");

        await service.ApplyMutationAsync(mutation, CancellationToken.None);
        // Same mutation, replayed (e.g. retried after a dropped response).
        await service.ApplyMutationAsync(mutation, CancellationToken.None);

        var stored = await store.GetAsync("Todo#todo-1", CancellationToken.None);
        Assert.Equal("Book Yael gym session", Assert.IsType<Todo>(stored).Title);
    }

    [Fact]
    public async Task Delete_produces_a_tombstone_visible_in_the_full_list()
    {
        var store = new InMemorySyncStore();
        var service = new SyncService(store);
        var t0 = DateTimeOffset.Parse("2026-07-11T08:00:00Z");

        await service.ApplyMutationAsync(MakeTodo("todo-1", t0, "device-1", "Reply to recruiter"), CancellationToken.None);
        await service.ApplyMutationAsync(
            MakeTodo("todo-1", t0.AddMinutes(1), "device-1", "Reply to recruiter", deleted: true), CancellationToken.None);

        var all = await service.GetAllAsync(CancellationToken.None);
        var tombstone = Assert.Single(all);
        Assert.True(tombstone.Deleted);
        Assert.True(tombstone.UpdatedAt > t0);
    }

    [Fact]
    public async Task Create_rejects_an_id_that_already_exists()
    {
        var store = new InMemorySyncStore();
        var service = new SyncService(store);
        var t0 = DateTimeOffset.Parse("2026-07-11T08:00:00Z");

        var firstCreate = await service.CreateAsync(MakeTodo("todo-1", t0, "device-1", "First"), CancellationToken.None);
        var secondCreate = await service.CreateAsync(MakeTodo("todo-1", t0.AddMinutes(1), "device-1", "Second"), CancellationToken.None);

        Assert.True(firstCreate);
        Assert.False(secondCreate);
        var stored = await store.GetAsync("Todo#todo-1", CancellationToken.None);
        Assert.Equal("First", Assert.IsType<Todo>(stored).Title);
    }

    [Fact]
    public async Task Two_clients_converge_to_the_same_state_via_shared_store()
    {
        var store = new InMemorySyncStore();
        var deviceService = new SyncService(store);
        var webService = new SyncService(store);
        var t0 = DateTimeOffset.Parse("2026-07-11T08:00:00Z");

        await deviceService.ApplyMutationAsync(
            MakeTodo("todo-1", t0, "device-1", "Focus 3: NOTICE file attribution"), CancellationToken.None);

        var seenByWeb = Assert.Single(await webService.GetAllAsync(CancellationToken.None));
        Assert.Equal("Focus 3: NOTICE file attribution", Assert.IsType<Todo>(seenByWeb).Title);

        var seenByDevice = Assert.Single(await deviceService.GetAllAsync(CancellationToken.None));
        Assert.Equal("Focus 3: NOTICE file attribution", Assert.IsType<Todo>(seenByDevice).Title);
    }
}
