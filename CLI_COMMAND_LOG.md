# CLI_COMMAND_LOG.md — GitHub Copilot CLI 작업 기록

> DailyMate는 GitHub Copilot(VS Code Copilot / Copilot CLI)만으로 개발되었습니다.
> 아래는 Copilot CLI 세션에서 수행한 주요 작업·명령 로그입니다.

## Phase 1 — Aspire 스캐폴딩 + 채팅 왕복

| 작업 | 명령/도구 |
|---|---|
| .NET 9/10 SDK 설치 | `winget install Microsoft.DotNet.SDK.9` / `Microsoft.DotNet.SDK.10` |
| React 웹 스캐폴딩 | `npm create vite@latest web -- --template react-ts` + `npm install` |
| Aspire 템플릿 설치 | `dotnet new install Aspire.ProjectTemplates` (13.5.2) |
| AppHost/ServiceDefaults 생성 | `dotnet new aspire-apphost` / `dotnet new aspire-servicedefaults` |
| api/agent/mcp-tool 생성 | `dotnet new web -n DailyMate.{Api,Agent,McpTool}` |
| 솔루션 구성·빌드 | `dotnet new sln` + `dotnet sln add ...` + `dotnet build DailyMate.slnx` |
| AppHost 등록 | `AddViteApp("web")` + `AddProject` ×3, `WithHttpHealthCheck("/health")`, `WithReference`/`WaitFor` 체인 |
| 채팅 왕복 검증 | web(Vite proxy) → api `POST /api/chat` → agent 에코, 브라우저 실측 |

## Phase 2 — 헬스 MCP 툴 (mcp-tool)

| 작업 | 명령/도구 |
|---|---|
| MCP 서버 패키지 | `dotnet add package ModelContextProtocol.AspNetCore --version 2.2.0` |
| 시드 DB | `Data/exercises.json` (chest/back/shoulders/legs/core, pattern 필드) |
| 툴 3종 구현 | `get_exercises` / `calc_intensity` / `build_routine` (`/mcp` streamable HTTP) |
| 강도 룰 | pain 전면 제외·fatigue RPE/세트 하향+머신 대체, volume short/normal/long, 워밍업 자동, estimated_minutes, pattern 중복 ≤2, core 보충 |
| 단독 검증 | Node `@modelcontextprotocol/sdk` 클라이언트로 툴 체인 호출 테스트 |

## Phase 2 — 헬스 코치 에이전트 (agent)

| 작업 | 명령/도구 |
|---|---|
| Copilot SDK + MAF | `dotnet add package Microsoft.Agents.AI.GitHub.Copilot --version 1.18.0` |
| 인증 | 로컬: 로그인된 Copilot CLI 세션 / 배포: `GH_TOKEN` 환경변수 (하드코딩 없음) |
| 에이전트 | 트리아지(발화→파라미터 JSON 추출) + 일기 챗, `CopilotClient.AsAIAgent()` |
| MCP 클라이언트 | agent→mcp-tool 호출은 Aspire 서비스 디스커버리로 해석 (URL 하드코딩 없음) |
| 세션 이력 | sessionId 단위 최근 10턴 유지 — 컨디션(통증/피로/선호) 누적 반영 |
| target 가드 | 부위 미확정 시 루틴 미생성 + 되묻기 (`data: null`) |

## UI — 루틴 카드

| 작업 | 명령/도구 |
|---|---|
| RoutineCard 컴포넌트 | `src/web/src/components/RoutineCard.tsx` + `.css` (독립·이식 가능) |
| 렌더 분기 | `data.type === "fitness_routine"` → 카드, 그 외 말풍선 |
| 검증 | Playwright 브라우저 실측 (T1~T4 시나리오 전부 통과) |

## 실행 방법

```bash
dotnet run --project src/DailyMate.AppHost
# 대시보드 URL이 출력되며 web/api/agent/mcp-tool 4개 서비스가 기동됨
```
