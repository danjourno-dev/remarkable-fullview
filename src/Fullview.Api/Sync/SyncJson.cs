using System.Text.Json;

namespace Fullview.Api.Sync;

/// <summary>
/// Shared JSON options for everything `/sync` touches — request/response bodies and the
/// entity blobs stored in DynamoDB. `JsonSerializerDefaults.Web` gives camelCase property
/// names and case-insensitive matching, which is what real HTTP clients (the web app,
/// the device, `System.Net.Http.Json`'s own defaults) send — plain
/// `JsonSerializer.Deserialize` without this is case-sensitive PascalCase-only and rejects
/// camelCase bodies as "invalid JSON".
/// </summary>
public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
