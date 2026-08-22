using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DailyMate.Agent;

/// <summary>
/// Health coach: calls the mcp-tool server (get_exercises → calc_intensity → build_routine)
/// and returns the fitness_routine JSON per the team contract (TRD §5).
/// </summary>
public sealed class HealthCoach(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HealthCoach> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;

    private async Task<McpClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;
        await _gate.WaitAsync(ct);
        try
        {
            if (_client is not null) return _client;
            // Aspire service discovery config (services__mcp-tool__*) — no hardcoded URL
            var baseUrl = configuration["services:mcp-tool:https:0"]
                          ?? configuration["services:mcp-tool:http:0"]
                          ?? throw new InvalidOperationException(
                              "mcp-tool endpoint not found in service discovery configuration.");
            var httpClient = httpClientFactory.CreateClient("mcp-tool");
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(new Uri(baseUrl), "/mcp"),
                Name = "dailymate-mcp-tool",
            }, httpClient, ownsHttpClient: false);
            _client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JsonNode> BuildRoutineAsync(TriageResult triage, CancellationToken ct)
    {
        var client = await GetClientAsync(ct);
        var target = triage.Target ?? "chest";

        // 1) exercise candidates for the target
        var exercises = await CallAsync(client, "get_exercises",
            new Dictionary<string, object?> { ["target"] = target }, ct);

        // 2) deterministic intensity parameters from the user's condition
        var intensity = await CallAsync(client, "calc_intensity", new Dictionary<string, object?>
        {
            ["fatigueLevel"] = triage.FatigueLevel ?? "normal",
            ["painAreas"] = triage.PainAreas ?? [],
            ["heavyPreference"] = triage.HeavyPreference,
            ["fatiguedAreas"] = triage.FatiguedAreas ?? [],
            ["volumeRequest"] = triage.VolumeRequest ?? "normal",
            ["equipmentPreference"] = triage.EquipmentPreference ?? "any",
        }, ct);

        // 3) final routine per the §5 contract
        var candidateNames = exercises["exercises"]!.AsArray()
            .Select(e => e!["name"]!.GetValue<string>()).ToArray();
        var extraNotes = triage.SameTargetYesterday
            ? new[] { "어제도 같은 부위를 운동했어요 — 근육 회복을 위해 오늘은 강도를 낮추고 컨디션을 살피며 진행하세요." }
            : Array.Empty<string>();
        var routine = await CallAsync(client, "build_routine", new Dictionary<string, object?>
        {
            ["target"] = target,
            ["volumeMultiplier"] = intensity["volume_multiplier"]!.GetValue<double>(),
            ["rpeCap"] = intensity["rpe_cap"]!.GetValue<int>(),
            ["equipmentPreference"] = intensity["equipment_preference"]!.GetValue<string>(),
            ["volumeRequest"] = intensity["volume_request"]!.GetValue<string>(),
            ["fatiguedAreas"] = triage.FatiguedAreas ?? [],
            ["painAreas"] = triage.PainAreas ?? [],
            ["reduceAccessoryCount"] = intensity["reduce_accessory_count"]!.GetValue<int>(),
            ["conditionSummary"] = triage.ConditionSummary ?? "특이사항 없음",
            ["candidateNames"] = candidateNames,
            ["extraNotes"] = extraNotes,
        }, ct);

        return routine;
    }

    public static string Summarize(JsonNode routine)
    {
        var exercises = routine["exercises"]!.AsArray();
        var lines = exercises.Select(e =>
        {
            var warmup = e!["is_warmup"]?.GetValue<bool>() == true ? " [워밍업]" : "";
            return $"- {e["name"]}{warmup} {e["sets"]}세트 × {e["reps"]}회 (RPE {e["rpe"]}, 휴식 {e["rest_sec"]}초)";
        });
        var notes = routine["notes"]?.GetValue<string>();
        var minutes = routine["estimated_minutes"]?.GetValue<int>();
        var summary = $"💪 오늘의 {routine["target"]} 루틴이야! 컨디션({routine["condition_summary"]})을 반영했어."
                      + (minutes is > 0 ? $" 예상 소요시간은 약 {minutes}분!" : "") + "\n\n"
                      + string.Join("\n", lines);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            summary += $"\n\n📝 {notes}";
        }
        return summary;
    }

    private async Task<JsonNode> CallAsync(
        McpClient client, string tool, IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        logger.LogInformation("Calling MCP tool {Tool}", tool);
        var result = await client.CallToolAsync(tool, args, cancellationToken: ct);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                   ?? throw new InvalidOperationException($"MCP tool '{tool}' returned no text content.");
        if (result.IsError == true)
        {
            throw new InvalidOperationException($"MCP tool '{tool}' failed: {text}");
        }
        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"MCP tool '{tool}' returned invalid JSON.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
        _gate.Dispose();
    }
}
