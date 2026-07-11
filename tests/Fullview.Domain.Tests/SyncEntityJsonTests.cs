using System.Text.Json;
using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Domain.Tests;

public class SyncEntityJsonTests
{
    [Fact]
    public void Todo_round_trips_through_the_polymorphic_base_type()
    {
        var original = new Todo
        {
            Id = "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            Context = SyncContext.Personal,
            UpdatedAt = DateTimeOffset.Parse("2026-07-11T09:00:00Z"),
            UpdatedBy = "device-1",
            Title = "Book Yael gym session",
            Priority = TodoPriority.Focus
        };

        var json = JsonSerializer.Serialize<SyncEntity>(original);
        var roundTripped = JsonSerializer.Deserialize<SyncEntity>(json);

        var todo = Assert.IsType<Todo>(roundTripped);
        Assert.Equal(original.Id, todo.Id);
        Assert.Equal(original.Title, todo.Title);
        Assert.Equal(original.Priority, todo.Priority);
        Assert.Equal(original.Context, todo.Context);
        Assert.Equal(original.UpdatedAt, todo.UpdatedAt);
    }

    [Fact]
    public void Discriminator_matches_the_entity_type_used_for_the_sort_key()
    {
        SyncEntity entity = new ShoppingItem
        {
            Id = "01ARZ3NDEKTSV4RRFFQ69G5FAW",
            Context = SyncContext.Personal,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "web",
            Name = "Milk"
        };

        var json = JsonSerializer.Serialize(entity);

        Assert.Contains("\"entityType\":\"ShoppingItem\"", json);
        Assert.Equal($"ShoppingItem#{entity.Id}", entity.SortKey);
    }

    [Fact]
    public void AgendaEvent_readonly_pulled_flag_round_trips()
    {
        SyncEntity original = new AgendaEvent
        {
            Id = "01ARZ3NDEKTSV4RRFFQ69G5FAX",
            Context = SyncContext.Work,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "calendar-puller",
            Title = "Standup",
            Start = DateTimeOffset.Parse("2026-07-13T09:00:00Z"),
            End = DateTimeOffset.Parse("2026-07-13T09:15:00Z"),
            Source = AgendaEventSource.GoogleCalendar,
            ExternalId = "google-event-id",
            ReadOnly = true
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = Assert.IsType<AgendaEvent>(JsonSerializer.Deserialize<SyncEntity>(json));

        Assert.True(roundTripped.ReadOnly);
        Assert.Equal(AgendaEventSource.GoogleCalendar, roundTripped.Source);
        Assert.Equal("google-event-id", roundTripped.ExternalId);
    }
}
