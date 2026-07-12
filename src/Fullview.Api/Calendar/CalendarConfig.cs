using System.Text.Json.Serialization;
using Fullview.Domain;

namespace Fullview.Api.Calendar;

/// <summary>One entry in the config-driven calendar list (Stage 6.5 design rule: adding,
/// removing, or re-tagging a calendar is a config change, not a code change). Read from
/// the plain-String SSM parameter named by FULLVIEW_GOOGLE_CALENDARS_PARAM.</summary>
public sealed class CalendarConfig
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("context")]
    public required SyncContext Context { get; init; }
}
