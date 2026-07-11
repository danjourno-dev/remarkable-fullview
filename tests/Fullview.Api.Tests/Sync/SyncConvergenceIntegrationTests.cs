using System.Net.Http.Json;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Domain.Sync;

namespace Fullview.Api.Tests.Sync;

/// <summary>
/// Stage 2 Done criteria: "integration test drives two fake clients to convergence
/// against deployed stack." Hits the real, already-deployed `/sync` endpoint over HTTP —
/// no AWS credentials needed, just the public API base URL, so it can't run in the
/// no-AWS-creds `ci.yml` job. Gated behind FULLVIEW_API_BASE_URL and excluded from the
/// default `dotnet test` run via the Category trait (see ci.yml and README).
///
/// Run manually once Stage 2 is deployed:
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

        var deviceOutbox = new SyncRequest
        {
            DeviceId = $"integration-device-{Guid.NewGuid():N}",
            Outbox =
            [
                new Todo
                {
                    Id = todoId,
                    Context = SyncContext.Personal,
                    UpdatedAt = now,
                    UpdatedBy = "integration-device",
                    Title = "Integration test todo"
                }
            ]
        };

        var deviceResponse = await http.PostAsJsonAsync("/sync", deviceOutbox);
        deviceResponse.EnsureSuccessStatusCode();
        var deviceResult = await deviceResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(deviceResult);

        // Second "fake client" (web) pulls from scratch and must see the device's write.
        var webPullRequest = new SyncRequest { DeviceId = $"integration-web-{Guid.NewGuid():N}", Outbox = [] };
        var webResponse = await http.PostAsJsonAsync("/sync", webPullRequest);
        webResponse.EnsureSuccessStatusCode();
        var webResult = await webResponse.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(webResult);

        var seenByWeb = webResult!.Delta.OfType<Todo>().FirstOrDefault(t => t.Id == todoId);
        Assert.NotNull(seenByWeb);
        Assert.Equal("Integration test todo", seenByWeb!.Title);

        // Web edits it (newer timestamp) and pushes back through the same endpoint.
        var webEdit = new SyncRequest
        {
            DeviceId = "integration-web",
            Cursor = webResult.Cursor,
            Outbox =
            [
                new Todo
                {
                    Id = todoId,
                    Context = SyncContext.Personal,
                    UpdatedAt = now.AddSeconds(1),
                    UpdatedBy = "integration-web",
                    Title = "Integration test todo (edited on web)"
                }
            ]
        };
        var webEditResponse = await http.PostAsJsonAsync("/sync", webEdit);
        webEditResponse.EnsureSuccessStatusCode();

        // Device pulls again from its old cursor and must converge on the web's edit.
        var deviceCatchUp = new SyncRequest
        {
            DeviceId = deviceOutbox.DeviceId,
            Cursor = deviceResult!.Cursor,
            Outbox = []
        };
        var deviceCatchUpResponse = await http.PostAsJsonAsync("/sync", deviceCatchUp);
        deviceCatchUpResponse.EnsureSuccessStatusCode();
        var deviceCatchUpResult = await deviceCatchUpResponse.Content.ReadFromJsonAsync<SyncResponse>();

        var converged = deviceCatchUpResult!.Delta.OfType<Todo>().FirstOrDefault(t => t.Id == todoId);
        Assert.NotNull(converged);
        Assert.Equal("Integration test todo (edited on web)", converged!.Title);
    }
}
