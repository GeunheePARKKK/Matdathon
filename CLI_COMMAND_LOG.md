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
| AppHost 등록 | `AddViteApp("web")` + `AddProject` ×3, `WithHttpHealthCheck("/health")`, `WithReference`/`WaitFor` 체인 |
| 채팅 왕복 검증 | web(Vite proxy) → api `POST /api/chat` → agent, 브라우저 실측 |

## Phase 2 — 헬스 MCP 툴 (mcp-tool)

| 작업 | 명령/도구 |
|---|---|
| MCP 서버 패키지 | `dotnet add package ModelContextProtocol.AspNetCore --version 2.2.0` |
| 시드 DB | `Data/exercises.json` (chest/back/shoulders/legs/core, pattern 필드) |
| 툴 3종 구현 | `get_exercises` / `calc_intensity` / `build_routine` (`/mcp` streamable HTTP) |
| 강도 룰 | pain 전면 제외 / fatigue RPE·세트 하향+머신 대체, volume short/normal/long, 워밍업 자동, estimated_minutes, pattern 중복 ≤2, core 보충 |
| 단독 검증 | Node `@modelcontextprotocol/sdk` 클라이언트로 툴 체인 호출 테스트 |

## Phase 2 — 에이전트 (agent)

| 작업 | 명령/도구 |
|---|---|
| Copilot SDK + MAF | `dotnet add package Microsoft.Agents.AI.GitHub.Copilot --version 1.18.0` |
| 인증 | 로컬: 로그인된 Copilot CLI 세션 / 배포: `GH_TOKEN` 환경변수 (하드코딩 없음) |
| 에이전트 4종 | Triage(파라미터 추출) · Interviewer(tool calling) · Writer(풍부화) · Health Coach(MCP 체인) |
| MCP 클라이언트 | agent→mcp-tool 호출은 Aspire 서비스 디스커버리로 해석 (URL 하드코딩 없음) |
| 컨텍스트 | 대화 이력 기반 컨디션 누적, target 미확정 시 되묻기, fatigue→pain 승격 |
| 폴백 | Copilot CLI 미탐지 시 Mock 모드 자동 전환 (전체 플로우 유지) |

## Phase 3 — 통합 (일기 에이전트 + 헬스 코치 병합)

| 작업 | 내용 |
|---|---|
| 저장소 병합 | 팀원 일기 에이전트(Detector/Interviewer/Writer + api/EF Core + 멀티페이지 UI)와 헬스 코치 파트 통합 |
| 핸드오프 | 인터뷰 SSE 스트림 중 루틴 요청 감지 → 헬스 코치 → 루틴 카드 → 인터뷰 복귀 |
| UI | RoutineCard 컴포넌트 이식 (`data.type === "fitness_routine"` 분기) |

## Phase 4 — 문서·제출 준비

| 작업 | 내용 |
|---|---|
| 시크릿 스캔 | 푸시 전 소스 전체 토큰/자격증명 하드코딩 검사 — 0건 |
| 문서 최종화 | docs/PRD.md · docs/TRD.md · agents.md를 실제 구현 기준으로 갱신 |
| 배포 | `azd auth login → azd init → azd provision → azd deploy` (azure.yaml, Aspire 통합) |

## 실행 방법

```bash
dotnet run --project DailyMate.AppHost
# 대시보드 URL이 출력되며 web/api/agent/mcp-tool 4개 서비스가 기동됨
```
