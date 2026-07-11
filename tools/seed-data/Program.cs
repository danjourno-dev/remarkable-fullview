using System.Net.Http.Json;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Domain.Sync;

var baseUrl = Environment.GetEnvironmentVariable("FULLVIEW_API_BASE_URL");
if (string.IsNullOrEmpty(baseUrl))
{
    Console.Error.WriteLine("Set FULLVIEW_API_BASE_URL to the deployed API's base URL (see the HttpApiUrl stack output), then re-run.");
    return 1;
}

var now = DateTimeOffset.UtcNow;

SyncEntity[] seed =
[
    new Todo
    {
        Id = Guid.NewGuid().ToString("N"),
        Context = SyncContext.Personal,
        UpdatedAt = now,
        UpdatedBy = "seed-data",
        Title = "Book Yael gym session",
        Priority = TodoPriority.Focus
    },
    new Todo
    {
        Id = Guid.NewGuid().ToString("N"),
        Context = SyncContext.Work,
        UpdatedAt = now,
        UpdatedBy = "seed-data",
        Title = "Reply to recruiter",
        Priority = TodoPriority.Normal
    },
    new ShoppingItem
    {
        Id = Guid.NewGuid().ToString("N"),
        Context = SyncContext.Personal,
        UpdatedAt = now,
        UpdatedBy = "seed-data",
        Name = "Milk",
        Category = "Dairy"
    },
    new Meal
    {
        Id = Guid.NewGuid().ToString("N"),
        Context = SyncContext.Personal,
        UpdatedAt = now,
        UpdatedBy = "seed-data",
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        Slot = MealSlot.Dinner,
        Description = "Chilli (batch)"
    }
];

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

var request = new SyncRequest { DeviceId = "seed-data", Outbox = seed.ToList() };
var response = await http.PostAsJsonAsync("/sync", request);
response.EnsureSuccessStatusCode();

var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
Console.WriteLine($"Seeded {seed.Length} entities. New cursor: {result?.Cursor}");
return 0;
