using System.Text.Json;
using System.Text.Json.Serialization;
using GitHub.Copilot;
using Microsoft.Agents.AI;

namespace DailyMate.Agent;

public record TriageResult(
    [property: JsonPropertyName("is_workout")] bool IsWorkout,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("fatigue_level")] string? FatigueLevel,
    [property: JsonPropertyName("pain_areas")] string[]? PainAreas,
    [property: JsonPropertyName("fatigued_areas")] string[]? FatiguedAreas,
    [property: JsonPropertyName("heavy_preference")] bool HeavyPreference,
    [property: JsonPropertyName("volume_request")] string? VolumeRequest,
    [property: JsonPropertyName("equipment_preference")] string? EquipmentPreference,
    [property: JsonPropertyName("same_target_yesterday")] bool SameTargetYesterday,
    [property: JsonPropertyName("condition_summary")] string? ConditionSummary);

/// <summary>
/// Hosts the GitHub Copilot SDK client and the Agent Framework agents built on top of it.
/// Auth: GH_TOKEN env var when present (deployment), otherwise the logged-in Copilot CLI session (local dev).
/// </summary>
public sealed class CopilotAgents(ILogger<CopilotAgents> logger) : IAsyncDisposable
{
    private const string TriageInstructions =
        """
        너는 DailyMate의 트리아지 에이전트다. [대화 이력]과 [현재 발화]를 함께 분석해 운동 루틴 요청인지 분류하고 파라미터를 추출한다.
        반드시 아래 JSON 형식으로만 응답한다. 다른 텍스트, 마크다운, 코드펜스를 절대 붙이지 않는다.

        {"is_workout": bool, "target": "chest|back|shoulders|legs" 또는 null, "fatigue_level": "low|normal|high", "pain_areas": [], "fatigued_areas": [], "heavy_preference": bool, "volume_request": "short|normal|long", "equipment_preference": "machine|free|any", "same_target_yesterday": bool, "condition_summary": "한 줄 요약"}

        규칙:
        - is_workout: 사용자가 운동할 예정이거나 운동을 원하면 true — **부위를 몰라도 true다.**
          예: "머신 위주로 하고 싶어", "오늘 운동 갈 거야", "루틴 짜줘" → 모두 true.
          운동 의도가 전혀 없는 일기/잡담/질문만 false.
        - target: 운동할 부위가 [현재 발화] 또는 [대화 이력]에 명시된 경우에만 채운다. 가슴=chest, 등=back, 어깨=shoulders, 하체/다리=legs.
          **부위가 어디에도 명시되지 않았으면 반드시 null. 절대 추측하거나 기본값을 넣지 않는다.**
          주의: "어깨가 결려/아파"는 컨디션 정보이지 target이 아니다. "어깨 운동할 거야"처럼 운동 의도가 명시된 경우만 target이다.
        - fatigue_level: 전신 피로도. 언급 없으면 "normal".
        - pain_areas / fatigued_areas: 다음 키만 사용: shoulder_front, wrist, elbow, lower_back, hamstring, knee.
          **명시적 통증 표현("아프다/통증/쑤시다")만 pain_areas**. "결리다/뻐근하다/피곤하다/힘이 없다/어제 했다"는 fatigued_areas.
          예: 어깨가 결린다 → fatigued_areas: ["shoulder_front"] / 어깨가 아프다 → pain_areas: ["shoulder_front"].
        - **컨디션 누적**: [대화 이력]에서 언급된 통증/피로/중량·장비·볼륨 선호는 이번 턴에 다시 말하지 않아도 반드시 유지해서 반영한다.
          단, 같은 부위가 이력에서 fatigue였다가 이번에 "아프다"고 명시하면 pain으로 승격한다.
        - heavy_preference: 고중량을 원하면 true. "고중량 싫어/가볍게"면 false. 언급 없으면 false.
        - volume_request: "1시간은 해야/너무 짧아/더 많이/길게" → "long", "가볍게/빨리/짧게/시간 없어" → "short", 언급 없으면 "normal".
        - equipment_preference: "머신 위주/프리웨이트 부담" → "machine", "프리웨이트로/바벨 위주" → "free", 언급 없으면 "any".
        - same_target_yesterday: [대화 이력]에서 사용자가 어제(전일) 오늘의 target과 같은 부위 웨이트 운동을 했다고 말한 경우에만 true.
        - condition_summary: 이력 포함 전체 컨디션을 한국어 한 줄로. 예: "왼쪽 어깨 결림, 머신 위주 선호".
        - is_workout이 false면 나머지 필드는 null 또는 빈 배열로 채운다.
        """;

    private const string ChatInstructions =
        """
        너는 DailyMate의 일기 도우미 에이전트다. 사용자의 하루 기록을 도와주는 따뜻한 한국어 어시스턴트로,
        간결하게(2~4문장) 응답한다. 의료 조언은 하지 않는다. 대화에 없는 사실을 창작하지 않는다.
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CopilotClient? _client;
    private AIAgent? _triage;
    private AIAgent? _chat;

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_client is not null) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_client is not null) return;

            var options = new CopilotClientOptions();
            var ghToken = Environment.GetEnvironmentVariable("GH_TOKEN");
            if (!string.IsNullOrWhiteSpace(ghToken))
            {
                options.GitHubToken = ghToken;
                logger.LogInformation("Copilot auth: GH_TOKEN environment variable.");
            }
            else
            {
                options.UseLoggedInUser = true;
                logger.LogInformation("Copilot auth: logged-in Copilot CLI session.");
            }

            var client = new CopilotClient(options);
            await client.StartAsync(ct);

            _triage = client.AsAIAgent(
                ownsClient: false,
                id: "triage",
                name: "TriageAgent",
                description: "운동 발화 분류 및 파라미터 추출",
                tools: null,
                instructions: TriageInstructions);
            _chat = client.AsAIAgent(
                ownsClient: false,
                id: "diary-chat",
                name: "DiaryChatAgent",
                description: "일반 일기 도우미 대화",
                tools: null,
                instructions: ChatInstructions);
            _client = client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TriageResult?> TriageAsync(string message, IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        await EnsureStartedAsync(ct);
        var response = await _triage!.RunAsync(BuildPrompt(message, history), cancellationToken: ct);
        var text = StripCodeFence(response.Text);
        try
        {
            return JsonSerializer.Deserialize<TriageResult>(text);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Triage output was not valid JSON: {Text}", text);
            return null;
        }
    }

    public async Task<string> ChatAsync(string message, IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        await EnsureStartedAsync(ct);
        var response = await _chat!.RunAsync(BuildPrompt(message, history), cancellationToken: ct);
        return response.Text;
    }

    private static string BuildPrompt(string message, IReadOnlyList<ChatTurn> history)
    {
        if (history.Count == 0) return message;
        var lines = history.Select(t => $"{t.Role}: {t.Content}");
        return $"[대화 이력]\n{string.Join("\n", lines)}\n\n[현재 발화]\n{message}";
    }

    private static string StripCodeFence(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var start = text.IndexOf('\n');
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                text = text[(start + 1)..end].Trim();
            }
        }
        // Tolerate leading/trailing prose around the JSON object
        var open = text.IndexOf('{');
        var close = text.LastIndexOf('}');
        return open >= 0 && close > open ? text[open..(close + 1)] : text;
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
