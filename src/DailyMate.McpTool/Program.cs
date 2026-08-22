using DailyMate.McpTool;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<ExerciseDb>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<FitnessTools>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Ok(new { service = "mcp-tool", status = "ok" }));

// MCP endpoint at /mcp (streamable HTTP)
app.MapMcp("/mcp");

app.Run();
