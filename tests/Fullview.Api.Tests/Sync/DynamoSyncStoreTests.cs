using Fullview.Api.Sync;
using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Api.Tests.Sync;

/// <summary>Covers the pure native-TTL policy DynamoSyncStore applies on write; the
/// DynamoDB round-trip itself needs no unit test (it's a plain PutItem).</summary>
public class DynamoSyncStoreTests
{
    private static Todo MakeTodo(DateTimeOffset updatedAt, bool completed) => new()
    {
        Id = "todo-1",
        Context = SyncContext.Personal,
        UpdatedAt = updatedAt,
        UpdatedBy = "web",
        Title = "Buy milk",
        Completed = completed
    };

    [Fact]
    public void Completed_todo_expires_24h_after_completion()
    {
        var completedAt = DateTimeOffset.Parse("2026-07-14T09:00:00Z");
        var ttl = DynamoSyncStore.CompletionTtlEpochSeconds(MakeTodo(completedAt, completed: true));

        Assert.Equal(completedAt.AddHours(24).ToUnixTimeSeconds(), ttl);
    }

    [Fact]
    public void Open_todo_never_expires()
    {
        var ttl = DynamoSyncStore.CompletionTtlEpochSeconds(
            MakeTodo(DateTimeOffset.Parse("2026-07-14T09:00:00Z"), completed: false));

        Assert.Null(ttl);
    }

    [Fact]
    public void Non_todo_entities_never_expire()
    {
        var meal = new Meal
        {
            Id = "meal-1",
            Context = SyncContext.Personal,
            UpdatedAt = DateTimeOffset.Parse("2026-07-14T09:00:00Z"),
            UpdatedBy = "web",
            Date = new DateOnly(2026, 7, 14),
            Slot = MealSlot.Dinner
        };

        Assert.Null(DynamoSyncStore.CompletionTtlEpochSeconds(meal));
    }
}
