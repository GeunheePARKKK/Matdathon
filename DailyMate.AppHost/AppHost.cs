var builder = DistributedApplication.CreateBuilder(args);

// mcp-tool: 헬스 코치용 MCP 서버 (get_exercises / calc_intensity / build_routine)
var mcpTool = builder.AddProject<Projects.DailyMate_McpTool>("mcp-tool");

// agent: Microsoft Agent Framework + Copilot SDK (LLM 키 없으면 Mock 모드)
var agent = builder.AddProject<Projects.DailyMate_Agent>("agent")
    .WithReference(mcpTool)
    .WaitFor(mcpTool)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "")
    .WithEnvironment("AZURE_OPENAI_KEY", Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "")
    .WithEnvironment("NOTION_MCP_TOKEN", Environment.GetEnvironmentVariable("NOTION_MCP_TOKEN") ?? "")
    .WithEnvironment("GOOGLE_CALENDAR_MCP_TOKEN", Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_MCP_TOKEN") ?? "");

// api: 일기/일정/통계 저장 (EF Core + SQLite)
var api = builder.AddProject<Projects.DailyMate_Api>("api")
    .WithReference(agent)
    .WaitFor(agent);

// web: React + Vite UI (배포 시 Dockerfile로 컨테이너화)
builder.AddViteApp("web", "../src/web")
    .WithReference(api)
    .WithReference(agent)
    .WaitFor(api)
    .WithHttpEndpoint(port: 5173, env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
