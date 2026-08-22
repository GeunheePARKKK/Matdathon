using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.GitHub.Copilot;

namespace DailyMate.Agent;

/// <summary>
/// Microsoft Agent Framework 기반 에이전트 런타임.
/// GitHub Copilot SDK(CopilotClient)가 사용 가능하면 LLM 모드, 아니면 Mock 모드로 동작한다. (TRD 6.2)
/// 단계(phase) 전환·활동 감지·일정 파싱은 항상 결정적 로직으로 처리하고,
/// LLM은 자연어 응답 생성에만 사용한다 — 하네스 엔지니어링.
/// </summary>
public sealed class AgentRuntime : IAsyncDisposable
{
    private CopilotClient? _client;

    public string Mode { get; private set; } = "mock";
    public AIAgent? Interviewer { get; private set; }
    public AIAgent? Writer { get; private set; }
    public AIAgent? Triage { get; private set; }

    public static async Task<AgentRuntime> CreateAsync(ILogger logger)
    {
        var runtime = new AgentRuntime();
        var cliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        var ghToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        var useLlm = Environment.GetEnvironmentVariable("DAILYMATE_LLM") != "off"
                     && (cliPath is not null || !string.IsNullOrWhiteSpace(ghToken) || IsOnPath("copilot"));
        if (!useLlm)
        {
            logger.LogInformation("Copilot CLI/토큰 미탐지 — Mock 모드로 기동합니다.");
            return runtime;
        }

        try
        {
            var options = new CopilotClientOptions();
            if (!string.IsNullOrWhiteSpace(ghToken))
            {
                options.GitHubToken = ghToken;
                logger.LogInformation("Copilot 인증: GH_TOKEN 환경 변수 (배포 모드)");
            }
            else
            {
                options.UseLoggedInUser = true;
                logger.LogInformation("Copilot 인증: 로그인된 Copilot CLI 세션 (로컬 모드)");
            }
            var client = new CopilotClient(options);
            await client.StartAsync();
            runtime._client = client;

            // 에이전트가 직접 호출하는 도구 (Agent Framework tool calling)
            var interviewerTools = new List<Microsoft.Extensions.AI.AIFunctionDeclaration>
            {
                Microsoft.Extensions.AI.AIFunctionFactory.Create(
                    (string text) => MockEngine.ParseSchedules(text),
                    "parse_schedule",
                    "내일 계획 자연어 텍스트에서 일정 목록(제목/시간/확정·미정)을 추출한다. 내일 계획 답변을 받으면 반드시 호출하라."),
                Microsoft.Extensions.AI.AIFunctionFactory.Create(
                    (string diaryText) => MockEngine.Topics(diaryText),
                    "list_topics",
                    "일기 원문에서 감지된 활동 주제 목록을 반환한다. 인터뷰 시작 시 호출해 질문 순서를 정하라."),
            };
            var writerTools = new List<Microsoft.Extensions.AI.AIFunctionDeclaration>
            {
                Microsoft.Extensions.AI.AIFunctionFactory.Create(
                    (string rawContent, string[] answers) =>
                        MockEngine.Enrich(rawContent, MockEngine.Topics(rawContent), [.. answers]).Enriched,
                    "draft_enrichment",
                    "원문과 답변 목록으로 결정적 보강 초안을 생성한다. 문체를 다듬기 전 참고용으로 호출할 수 있다."),
            };

            runtime.Interviewer = new GitHubCopilotAgent(client,
                name: "Interviewer", description: "일기 심화 질문 에이전트",
                tools: interviewerTools,
                instructions: AgentDefinitions.InterviewerInstructions);
            runtime.Writer = new GitHubCopilotAgent(client,
                name: "Writer", description: "일기 풍부화 작가 에이전트",
                tools: writerTools,
                instructions: AgentDefinitions.WriterInstructions);
            runtime.Triage = new GitHubCopilotAgent(client,
                name: "Triage", description: "운동 루틴 요청 분류·컨디션 추출 에이전트",
                instructions: AgentDefinitions.TriageInstructions);
            runtime.Mode = "copilot";
            logger.LogInformation("GitHub Copilot SDK + Agent Framework LLM 모드로 기동했습니다. (도구 {N}개 등록)", interviewerTools.Count + writerTools.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Copilot 초기화 실패 — Mock 모드로 폴백합니다.");
            runtime._client = null;
            runtime.Interviewer = runtime.Writer = runtime.Triage = null;
            runtime.Mode = "mock";
        }
        return runtime;
    }

    private static bool IsOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Any(dir => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, exe)));

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }
}
