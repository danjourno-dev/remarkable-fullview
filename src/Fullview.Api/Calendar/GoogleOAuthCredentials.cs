using System.Text.Json.Serialization;

namespace Fullview.Api.Calendar;

/// <summary>Shape of the JSON stored in the SecureString SSM parameter created by hand in
/// checkpoint 6.5.1 (see docs/device-setup.md). Desktop-app OAuth client, so there is no
/// redirect URI to host.</summary>
public sealed class GoogleOAuthCredentials
{
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    [JsonPropertyName("clientSecret")]
    public required string ClientSecret { get; init; }
}
