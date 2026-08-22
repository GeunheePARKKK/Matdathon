var builder = DistributedApplication.CreateBuilder(args);

// mcp-tool: 헬스 코치용 MCP 서버 (get_exercises / calc_intensity / build_routine)
var mcpTool = builder.AddProject<Projects.DailyMate_McpTool>("mcp-tool");

// agent: Microsoft Agent Framework + Copilot SDK (GH_TOKEN 없으면 Mock 모드)
var agent = builder.AddProject<Projects.DailyMate_Agent>("agent")
    .WithReference(mcpTool)
    .WaitFor(mcpTool)
    .WithEnvironment("GH_TOKEN", Environment.GetEnvironmentVariable("GH_TOKEN") ?? "")
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "")
    .WithEnvironment("AZURE_OPENAI_KEY", Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "")
    .WithEnvironment("NOTION_MCP_TOKEN", Environment.GetEnvironmentVariable("NOTION_MCP_TOKEN") ?? "")
    .WithEnvironment("GOOGLE_CALENDAR_MCP_TOKEN", Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_MCP_TOKEN") ?? "");

// api: 일기/일정/통계 저장 (EF Core + SQLite) — 배포 시 web 정적 파일 서빙 + /agent 프록시 겸함
var api = builder.AddProject<Projects.DailyMate_Api>("api")
    .WithReference(agent)
    .WaitFor(agent)
    .WithExternalHttpEndpoints();

// web: React + Vite UI
// - 로컬(run): Vite 개발 서버로 실행
// - 배포(publish): 빌드된 정적 파일을 api가 wwwroot로 직접 서빙 (docker 불필요, 컨테이너 1개 절감)
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddViteApp("web", "../src/web")
        .WithReference(api)
        .WithReference(agent)
        .WaitFor(api)
        .WithHttpEndpoint(port: 5173, env: "PORT")
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
