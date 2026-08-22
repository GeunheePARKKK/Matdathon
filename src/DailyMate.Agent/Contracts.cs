using System.Text.Json;

namespace DailyMate.Agent;

public record ChatMessageDto(string Role, string Content, string? Kind = null);
public record ChatRequest(string DiaryText, ChatMessageDto[] History);
public record DetectRequest(string Text);
public record EnrichRequest(string RawContent, JsonElement Metadata, JsonElement? Photos);
public record ScheduleParseRequest(string Text);

public record DetectedSpan(int Start, int End, string Type);

public record ScheduleDto(string Id, string Title, string Datetime, string Status, string Source, bool Done);
