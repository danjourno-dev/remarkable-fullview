using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Device.Storage;

/// <summary>
/// Fabricated demo data for Checkpoint 4.1 ("Dan lives with the seeded board for a day").
/// Covers both Personal and Work contexts so mode toggling has something to show. Only
/// applied when the store is empty (<see cref="ApplyIfEmpty"/>) — never overwrites real data.
/// </summary>
public static class SeedData
{
    private const string SeedDevice = "seed";

    public static void ApplyIfEmpty(DeviceStore store)
    {
        if (store.Query<Todo>().Count > 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        foreach (var todo in Todos(now))
        {
            store.SaveSeed(todo);
        }

        foreach (var agendaEvent in AgendaEvents(now))
        {
            store.SaveSeed(agendaEvent);
        }

        foreach (var meal in Meals(now, today))
        {
            store.SaveSeed(meal);
        }

        foreach (var item in ShoppingItems(now))
        {
            store.SaveSeed(item);
        }

        foreach (var recipe in Recipes(now))
        {
            store.SaveSeed(recipe);
        }
    }

    private static IEnumerable<Todo> Todos(DateTimeOffset now) =>
    [
        new()
        {
            Id = "seed-todo-1",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Book Yael's gym session",
            Priority = TodoPriority.Focus,
            Energy = TodoEnergy.QuickWin
        },
        new()
        {
            Id = "seed-todo-2",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Renew car insurance",
            Priority = TodoPriority.Normal,
            DueDate = DateOnly.FromDateTime(now.LocalDateTime).AddDays(3)
        },
        new()
        {
            Id = "seed-todo-3",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Plan weekend hike",
            Priority = TodoPriority.Someday
        },
        new()
        {
            Id = "seed-todo-4",
            Context = SyncContext.Work,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Reply to recruiter email",
            Priority = TodoPriority.Focus,
            Energy = TodoEnergy.QuickWin
        },
        new()
        {
            Id = "seed-todo-5",
            Context = SyncContext.Work,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Draft Q3 architecture doc",
            Priority = TodoPriority.Normal,
            Energy = TodoEnergy.Deep
        }
    ];

    private static IEnumerable<AgendaEvent> AgendaEvents(DateTimeOffset now) =>
    [
        new()
        {
            Id = "seed-agenda-1",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Dentist appointment",
            Start = now.Date.AddHours(17),
            End = now.Date.AddHours(18)
        },
        new()
        {
            Id = "seed-agenda-2",
            Context = SyncContext.Work,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Sprint planning",
            Start = now.Date.AddHours(10),
            End = now.Date.AddHours(11)
        },
        new()
        {
            Id = "seed-agenda-3",
            Context = SyncContext.Work,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "1:1 with manager",
            Start = now.Date.AddHours(14),
            End = now.Date.AddHours(14.5)
        }
    ];

    private static IEnumerable<Meal> Meals(DateTimeOffset now, DateOnly today) =>
    [
        new()
        {
            Id = "seed-meal-1",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Date = today,
            Slot = MealSlot.Breakfast,
            Description = "Porridge with berries"
        },
        new()
        {
            Id = "seed-meal-2",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Date = today,
            Slot = MealSlot.Dinner,
            RecipeId = "seed-recipe-1",
            Description = "Chicken traybake"
        }
    ];

    private static IEnumerable<ShoppingItem> ShoppingItems(DateTimeOffset now) =>
    [
        new()
        {
            Id = "seed-shopping-1",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Name = "Chicken thighs",
            Category = "Meat"
        },
        new()
        {
            Id = "seed-shopping-2",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Name = "New potatoes",
            Category = "Veg"
        },
        new()
        {
            Id = "seed-shopping-3",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Name = "Oats",
            Category = "Pantry",
            Checked = true
        }
    ];

    private static IEnumerable<Recipe> Recipes(DateTimeOffset now) =>
    [
        new()
        {
            Id = "seed-recipe-1",
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = SeedDevice,
            Title = "Chicken traybake",
            Ingredients =
            [
                "8 chicken thighs",
                "New potatoes",
                "Red onion",
                "Olive oil",
                "Paprika"
            ],
            Steps =
            [
                "Preheat oven to 200C",
                "Toss everything in oil and paprika",
                "Roast 40 minutes, turning once"
            ]
        }
    ];
}
