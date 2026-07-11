using System.Text.Json;

namespace Fullview.Device.Storage;

/// <summary>Same convention as Fullview.Api.Sync.SyncJson: camelCase + case-insensitive, so
/// entities round-trip identically whether they came from the device store or the sync API.</summary>
internal static class DeviceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
