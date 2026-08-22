var builder = DistributedApplication.CreateBuilder(args);

var mcpTool = builder.AddProject<Projects.DailyMate_McpTool>("mcp-tool")
    .WithHttpHealthCheck("/health");

var agent = builder.AddProject<Projects.DailyMate_Agent>("agent")
    .WithHttpHealthCheck("/health")
    .WithReference(mcpTool)
    .WaitFor(mcpTool);

var api = builder.AddProject<Projects.DailyMate_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithReference(agent)
    .WaitFor(agent)
    .WithExternalHttpEndpoints();

builder.AddViteApp("web", "../web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
