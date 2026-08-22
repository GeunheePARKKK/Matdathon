using System.Text.Json;
using System.Text.Json.Serialization;

namespace DailyMate.Api;

public record DiaryEntry(
    string Date,
    string RawContent,
    string EnrichedContent,
    string[] Hashtags,
    Photo[] Photos,
    JsonElement? Metadata,
    string CreatedAt);

public record Photo(string Id, string Filename, string? Caption, string? LinkedTopic);

public class Schedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Datetime { get; set; } = "";           // ISO 8601
    public string Status { get; set; } = "confirmed";    // confirmed | tentative
    public string Source { get; set; } = "tomorrow_plan";
    public bool Done { get; set; }
}

public class DiaryRow
{
    public string Date { get; set; } = "";
    public string RawContent { get; set; } = "";
    public string EnrichedContent { get; set; } = "";
    public string HashtagsJson { get; set; } = "[]";
    public string PhotosJson { get; set; } = "[]";
    public string MetadataJson { get; set; } = "{}";
    public string CreatedAt { get; set; } = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DiaryEntry ToDto() => new(
        Date, RawContent, EnrichedContent,
        JsonSerializer.Deserialize<string[]>(HashtagsJson, Json) ?? [],
        JsonSerializer.Deserialize<Photo[]>(PhotosJson, Json) ?? [],
        JsonSerializer.Deserialize<JsonElement>(MetadataJson),
        CreatedAt);

    public static DiaryRow FromDto(DiaryEntry e) => new()
    {
        Date = e.Date,
        RawContent = e.RawContent,
        EnrichedContent = e.EnrichedContent,
        HashtagsJson = JsonSerializer.Serialize(e.Hashtags, Json),
        PhotosJson = JsonSerializer.Serialize(e.Photos, Json),
        MetadataJson = e.Metadata is null ? "{}" : JsonSerializer.Serialize(e.Metadata, Json),
        CreatedAt = string.IsNullOrEmpty(e.CreatedAt) ? DateTime.UtcNow.ToString("o") : e.CreatedAt
    };
}
