using System.Net.Http.Json;
using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Api.Tests.Sync;

/// <summary>
/// "Two fake clients converge" against the real, already-deployed `/entities` endpoint over
/// HTTP — no AWS credentials needed, just the public API base URL, so it can't run in the
/// no-AWS-creds `ci.yml` job. Gated behind FULLVIEW_API_BASE_URL and excluded from the
/// default `dotnet test` run via the Category trait (see ci.yml and README).
///
/// Run manually once deployed:
///   FULLVIEW_API_BASE_URL=https://&lt;api-id&gt;.execute-api.eu-west-2.amazonaws.com \
///     dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public class SyncConvergenceIntegrationTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("FULLVIEW_API_BASE_URL");

    [Fact]
    public async Task Two_clients_converge_after_syncing_against_the_deployed_stack()
    {
        if (string.IsNullOrEmpty(BaseUrl))
        {
            // Not configured — this suite only runs when explicitly invoked against a
            // deployed stack (see class doc comment). Not a failure.
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        var todoId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        // "Device" creates a todo.
        var deviceTodo = new Todo
        {
            Id = todoId,
            Context = SyncContext.Personal,
            UpdatedAt = now,
            UpdatedBy = "integration-device",
            Title = "Integration test todo"
        };
        var deviceResponse = await http.PutAsJsonAsync<SyncEntity>($"/entities/{todoId}", deviceTodo);
        deviceResponse.EnsureSuccessStatusCode();

        // Second "fake client" (web) does a full pull and must see the device's write.
        var webPull = await http.GetFromJsonAsync<List<SyncEntity>>("/entities");
        var seenByWeb = webPull?.OfType<Todo>().FirstOrDefault(t => t.Id == todoId);
        Assert.NotNull(seenByWeb);
        Assert.Equal("Integration test todo", seenByWeb!.Title);

        // Web edits it (newer timestamp) and pushes back through the same endpoint.
        var webTodo = new Todo
        {
            Id = todoId,
            Context = SyncContext.Personal,
            UpdatedAt = now.AddSeconds(1),
            UpdatedBy = "integration-web",
            Title = "Integration test todo (edited on web)"
        };
        var webEditResponse = await http.PutAsJsonAsync<SyncEntity>($"/entities/{todoId}", webTodo);
        webEditResponse.EnsureSuccessStatusCode();

        // Device pulls again and must converge on the web's edit.
        var deviceCatchUp = await http.GetFromJsonAsync<List<SyncEntity>>("/entities");
        var converged = deviceCatchUp?.OfType<Todo>().FirstOrDefault(t => t.Id == todoId);
        Assert.NotNull(converged);
        Assert.Equal("Integration test todo (edited on web)", converged!.Title);
    }
}
