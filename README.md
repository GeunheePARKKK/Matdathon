# 🌙 DailyMate — 개인 생산성 향상 에이전트

일기를 쓰면 에이전트가 문맥을 감지해 심화 질문을 던지고, 답변으로 일기를 풍부하게 완성하며, 내일 일정까지 등록해주는 웹앱.

## 스택 (해커톤 필수 요건)
- **Web**: React 18 + TypeScript + Vite + Zustand
- **API**: ASP.NET Core Minimal API + EF Core (SQLite)
- **Agent**: Microsoft Agent Framework + GitHub Copilot SDK (`Microsoft.Agents.AI.GitHub.Copilot`)
- **MCP**: Notion / Google Calendar (토큰 미설정 시 자동 Mock 모드)
- **Infra**: .NET Aspire (오케스트레이션 · 서비스 디스커버리 · OTel · 헬스체크)
- 로그인/인증 없음

## 로컬 실행
```bash
# 요구: .NET 10 SDK, Node.js 20+
cd src/web && npm install && cd ../..
dotnet run --project DailyMate.AppHost   # 전체 기동
# web: http://localhost:5173 · Aspire 대시보드 URL은 콘솔 출력 참조
```

## 환경 변수 (선택 — 없으면 Mock 모드)
```
COPILOT_CLI_PATH          # GitHub Copilot CLI 경로 → LLM 모드 활성화 (PATH에 있으면 자동 감지)
NOTION_MCP_TOKEN / NOTION_DATABASE_ID
GOOGLE_CALENDAR_MCP_TOKEN
DAILYMATE_LLM=off         # LLM 강제 비활성화 (데모 시 빠른 목 모드)
```

## Azure 배포 (azd + Aspire)
```bash
azd auth login
azd init            # 환경 이름 입력 (예: dailymate-prod)
azd provision       # ① 배포 계획·검증 — Container Apps ×4, ACR, Log Analytics 생성
azd deploy          # ② 배포 실행 — AppHost가 서비스 토폴로지를 컨테이너로 변환
```
- web은 [Dockerfile](src/web/Dockerfile)로 컨테이너화되며, [server.mjs](src/web/server.mjs)가 Aspire 서비스 디스커버리 env로 api/agent에 프록시
- Aspire OTel → Azure Monitor 연동으로 관찰 가능성 확보

## 구조
```
DailyMate.AppHost/        # Aspire 진입점 (web + api + agent + mcp-tool 오케스트레이션)
DailyMate.ServiceDefaults/# OTel · 헬스체크 · 리질리언스 공통
src/web/                  # React UI (홈/일기작성/에이전트대화/완성/기록/연동 + 루틴 카드)
src/DailyMate.Api/        # 일기·일정·통계·내보내기 REST API
src/DailyMate.Agent/      # Triage/Interviewer/Writer/HealthCoach 에이전트 + MCP + SSE 채팅
src/DailyMate.McpTool/    # 자체 MCP 서버 — 운동 툴 3종 (get_exercises/calc_intensity/build_routine)
docs/                     # PRD.md · TRD.md
agents.md                 # 에이전트 역할·바운더리 정의 (SSOT)
CLI_COMMAND_LOG.md        # Copilot CLI 작업 기록
```
