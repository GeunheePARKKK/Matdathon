using System.Text.Json.Serialization;
using DailyMate.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Resolved via Aspire service discovery (no hardcoded URL).
// MCP streamable HTTP는 장수명 스트림이라 resilience 타임아웃에서 제외한다.
builder.Services.AddHttpClient("mcp-tool", client =>
{
    client.BaseAddress = new Uri("https+http://mcp-tool");
    client.Timeout = Timeout.InfiniteTimeSpan;
}).RemoveAllResilienceHandlers();
builder.Services.AddSingleton<CopilotAgents>();
builder.Services.AddSingleton<HealthCoach>();
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Ok(new { service = "agent", status = "ok" }));

string[] validTargets = ["chest", "back", "shoulders", "legs"];

app.MapPost("/chat", async (ChatRequest request, CopilotAgents agents, HealthCoach coach,
    SessionStore sessions, ILogger<Program> logger, CancellationToken ct) =>
{
    var history = sessions.GetHistory(request.SessionId);
    var triage = await agents.TriageAsync(request.Message, history, ct);
    ChatResponse response;

    if (triage is { IsWorkout: true })
    {
        var target = triage.Target?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(target) || !validTargets.Contains(target))
        {
            // target을 발화/이력에서 확인할 수 없으면 루틴을 만들지 않고 되묻는다
            response = new ChatResponse("assistant",
                "오늘 어느 부위 운동하실 예정이에요? (가슴 / 등 / 어깨 / 하체 중에 알려주시면 컨디션에 맞춰 루틴을 짜드릴게요)",
                null);
        }
        else
        {
            try
            {
                var routine = await coach.BuildRoutineAsync(triage, ct);
                response = new ChatResponse("assistant", HealthCoach.Summarize(routine), routine);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health coach routine build failed; falling back to chat.");
                response = new ChatResponse("assistant", await agents.ChatAsync(request.Message, history, ct), null);
            }
        }
    }
    else
    {
        response = new ChatResponse("assistant", await agents.ChatAsync(request.Message, history, ct), null);
    }

    sessions.Append(request.SessionId, "user", request.Message);
    sessions.Append(request.SessionId, "assistant", response.Content);
    return Results.Ok(response);
});

app.Run();

record ChatRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("sessionId")] string? SessionId);

// data: structured payloads (e.g. fitness_routine JSON per TRD §5)
record ChatResponse(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("data")] object? Data);
