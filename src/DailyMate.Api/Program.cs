var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Resolved via Aspire service discovery (no hardcoded URL).
// LLM 호출은 기본 resilience 타임아웃(10s/30s)보다 오래 걸리므로 이 클라이언트는 제외한다.
builder.Services.AddHttpClient("agent", client =>
{
    client.BaseAddress = new Uri("https+http://agent");
    client.Timeout = TimeSpan.FromMinutes(5);
}).RemoveAllResilienceHandlers();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Ok(new { service = "api", status = "ok" }));

app.MapPost("/api/chat", async (ChatRequest request, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    var client = httpClientFactory.CreateClient("agent");
    var response = await client.PostAsJsonAsync("/chat", request, ct);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
    return Results.Ok(body);
});

app.Run();

record ChatRequest(string Message, string? SessionId);

record ChatResponse(string Role, string Content, object? Data);
