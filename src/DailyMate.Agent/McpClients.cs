namespace DailyMate.Agent;

public record McpResult(bool Ok, bool Mock, string Message, string? Url = null);

/// <summary>MCP 도구 클라이언트 추상화 — 토큰 부재 시 Mock 모드 (agents.md §4, TRD 6.2).</summary>
public interface IMcpToolClient
{
    string Name { get; }
    bool IsMock { get; }
    Task<McpResult> ExecuteAsync(string action, System.Text.Json.JsonElement payload, CancellationToken ct = default);
}

public sealed class MockMcpClient(string name) : IMcpToolClient
{
    public string Name => name;
    public bool IsMock => true;

    public Task<McpResult> ExecuteAsync(string action, System.Text.Json.JsonElement payload, CancellationToken ct = default) =>
        Task.FromResult(new McpResult(true, true,
            $"{Name} {action} 완료 (목 모드)",
            Name == "notion" ? $"https://notion.so/mock-page-{Guid.NewGuid():N}" : null));
}

/// <summary>Notion MCP — 토큰과 데이터베이스 ID가 있으면 실제 페이지를 생성한다.</summary>
public sealed class NotionMcpClient(HttpClient http, string token, string databaseId) : IMcpToolClient
{
    public string Name => "notion";
    public bool IsMock => false;

    public async Task<McpResult> ExecuteAsync(string action, System.Text.Json.JsonElement payload, CancellationToken ct = default)
    {
        var date = payload.TryGetProperty("date", out var d) ? d.GetString() : DateTime.Today.ToString("yyyy-MM-dd");
        var content = payload.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var body = new
        {
            parent = new { database_id = databaseId },
            properties = new Dictionary<string, object>
            {
                ["Name"] = new { title = new[] { new { text = new { content = $"{date} 일기" } } } }
            },
            children = content.Split("\n\n").Take(20).Select(p => new
            {
                @object = "block",
                type = "paragraph",
                paragraph = new { rich_text = new[] { new { type = "text", text = new { content = p } } } }
            })
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.notion.com/v1/pages");
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Headers.Add("Notion-Version", "2022-06-28");
        req.Content = System.Net.Http.Json.JsonContent.Create(body);
        var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            return new McpResult(false, false, $"Notion 저장 실패 ({(int)res.StatusCode}) — 로컬 저장은 유지됩니다.");
        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
        var url = json.TryGetProperty("url", out var u) ? u.GetString() : null;
        return new McpResult(true, false, "Notion 페이지 생성 완료", url);
    }
}

public static class McpClientFactory
{
    public static IMcpToolClient CreateNotion(IServiceProvider sp)
    {
        var token = Environment.GetEnvironmentVariable("NOTION_MCP_TOKEN");
        var db = Environment.GetEnvironmentVariable("NOTION_DATABASE_ID");
        return string.IsNullOrEmpty(token) || string.IsNullOrEmpty(db)
            ? new MockMcpClient("notion")
            : new NotionMcpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("notion"), token, db);
    }

    public static IMcpToolClient CreateCalendar()
    {
        // Google Calendar는 OAuth 플로우가 필요해 해커톤 범위에서는 목 모드로 제공 (PRD F5-2 P1)
        return new MockMcpClient("calendar");
    }
}
