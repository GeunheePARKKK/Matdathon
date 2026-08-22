# 🛠 TRD — DailyMate (기술 요구사항 문서)

> Technical Requirements Document
> 버전: 1.0 (최종) · 작성일: 2026-08-22

---

## 1. 기술 스택

| 계층 | 기술 | 비고 |
|---|---|---|
| Frontend | React 18 + TypeScript + Vite | 상태관리: Zustand (최소화), 모바일 우선 반응형 |
| Backend API | ASP.NET Core (.NET 10) Minimal API | EF Core + SQLite |
| AI/LLM Layer | **GitHub Copilot SDK** (`GitHub.Copilot.SDK`) + **Microsoft Agent Framework** (`Microsoft.Agents.AI.GitHub.Copilot`) | 해커톤 필수 — LLM은 Copilot 구독으로 연결 |
| Tool Layer | **MCP** — 자체 운동 툴 서버(`mcp-tool`, ModelContextProtocol.AspNetCore) + Notion/Google Calendar 클라이언트 | 목 모드 지원 필수 |
| Infra Layer | **.NET Aspire 13.5** | 오케스트레이션·디스커버리·OTel·헬스체크 |
| 저장소 | SQLite (EF Core) + 파일(사진) | 로그인 없음 → 로컬 세션 키 기반 |
| 배포 | Azure Container Apps (azd + Aspire 통합) | `azure.yaml` 1파일로 반복 가능 배포 |

---

## 2. 시스템 아키텍처

```
┌───────────────────────────────────────────────────────────────┐
│                    .NET Aspire AppHost                         │
│      (서비스 디스커버리 · OpenTelemetry · 헬스체크)              │
└───────────────────────────────────────────────────────────────┘
     │              │                │                  │
     ▼              ▼                ▼                  ▼
┌──────────┐  ┌──────────────┐  ┌────────────────┐  ┌───────────────────┐
│ web      │  │ api          │  │ agent          │  │ mcp-tool          │
│ React    │─▶│ ASP.NET      │─▶│ MAF + Copilot  │─▶│ MCP 서버          │
│ + Vite   │  │ Minimal API  │  │ SDK (4 agents) │  │ (운동 툴 3종)      │
└──────────┘  └──────┬───────┘  └───────┬────────┘  └───────────────────┘
                     │                  │
                     ▼                  ▼
              ┌────────────┐   ┌──────────────────┐
              │ SQLite     │   │ 외부 MCP          │
              │ + photos/  │   │ · Notion          │
              └────────────┘   │ · Google Calendar │
                               └──────────────────┘
```

### 서비스 간 통신 (URL 하드코딩 금지)
- `web → api/agent`: REST + SSE — 배포 시 [server.mjs](../src/web/server.mjs)가 Aspire가 주입한 서비스 디스커버리 env로 프록시
- `agent → mcp-tool`: MCP streamable HTTP — `services:mcp-tool:*` 디스커버리 설정으로 엔드포인트 해석
- `agent → LLM`: GitHub Copilot SDK (`CopilotClient`) — Copilot CLI 프로세스를 통해 모델 연결
- `agent → 외부`: Notion/Calendar 클라이언트 (토큰 부재 시 Mock)

---

## 3. GitHub Copilot SDK + Microsoft Agent Framework 활용 설계

> 두 필수 기술이 앱의 **핵심 경로**에서 어떻게 깊이 있게 쓰이는지 정의한다.

### 3.1 모델 연결 (Copilot SDK)
- `CopilotClient(new CopilotClientOptions { UseLoggedInUser = true })` — 로컬은 로그인된 Copilot CLI 세션, 배포는 `GH_TOKEN` 환경 변수로 인증 (키 하드코딩 없음)
- SDK 빌드 타겟이 copilot CLI 바이너리를 산출물에 번들 → 컨테이너 배포에서도 동일하게 동작
- CLI 미탐지·초기화 실패 시 **Mock 모드로 자동 폴백** ([AgentRuntime.cs](../src/DailyMate.Agent/AgentRuntime.cs)) — 시연 안전장치

### 3.2 에이전트 구성 (Agent Framework `AIAgent` 4종)
| 에이전트 | 구현 | 역할 |
|---|---|---|
| Triage | `GitHubCopilotAgent` + JSON 강제 인스트럭션 | 운동 발화 분류 + 컨디션 파라미터 추출 (규칙 기반 `MockTriage` 폴백) |
| Interviewer | `GitHubCopilotAgent` + **tool calling** | 주제별 심화 질문 — `parse_schedule`, `list_topics` AIFunction 도구 호출 |
| Writer | `GitHubCopilotAgent` + tool calling | 일기 풍부화 — `draft_enrichment` 도구로 결정적 초안 참고 |
| Health Coach | Triage 결과 → **MCP 툴 체인 오케스트레이션** | `get_exercises → calc_intensity → build_routine` 순차 호출 |

### 3.3 오케스트레이션·컨텍스트·스트리밍
- **핸드오프**: 인터뷰 진행 중 루틴 요청 감지 → 헬스 코치로 위임 → 루틴 카드 반환 후 인터뷰 복귀 ([ChatOrchestrator.cs](../src/DailyMate.Agent/ChatOrchestrator.cs))
- **컨텍스트 처리**: 대화 이력(history)을 트리아지 프롬프트에 포함 — 이전 턴의 통증/피로/선호가 후속 루틴에 누적 반영, fatigue→pain 승격 규칙
- **스트리밍**: `/agent/chat`은 SSE(`text/event-stream`)로 토큰·이벤트 단위 전송 → 대기 체감 최소화
- **하네스 엔지니어링**: 단계 전환·활동 감지·일정 파싱·강도 계산은 결정적 코드/MCP 툴이 담당, LLM은 자연어 이해·표현에 집중 → 환각 완화

---

## 4. MCP 설계

### 4.1 자체 MCP 서버 — mcp-tool (운동 도구 3종)
`ModelContextProtocol.AspNetCore` 기반 streamable HTTP 서버 (`/mcp`).

| 툴 | 입력 | 출력 |
|---|---|---|
| `get_exercises` | `target`, `equipment?` | 부위별 운동 후보 (시드 JSON: 부위 5개 × 5~11종, `pattern` 필드) |
| `calc_intensity` | `fatigue_level`, `pain_areas[]`, `fatigued_areas[]`, `heavy_preference`, `volume_request`, `equipment_preference` | `volume_multiplier`, `rpe_cap`, 장비/볼륨 해석, notes |
| `build_routine` | 후보 + 강도 파라미터 + `extra_notes[]` | `fitness_routine` JSON (§5 계약) |

**강도 룰 (결정적 계산):**
- **pain**(명시적 "아프다/통증") → 해당 joint_load 운동 **전면 제외** + 전문의 안내
- **fatigue**(결림/뻐근/피곤) → 제외하지 않고 RPE −1·세트 −1, 프리웨이트는 머신/케이블 대체 우선
- heavy_preference=false → RPE 상한 7 / equipment_preference=machine → 머신·케이블 우선
- volume_request: short=3종목 / normal=4 / long=8종목+세트+1 (후보 부족 시 core 보충 → 그래도 부족하면 유산소 안내)
- 조립: compound→isolation 정렬, 같은 `pattern` 최대 2개, 워밍업(RPE 4) 자동 삽입, `estimated_minutes` 계산

### 4.2 외부 MCP — Notion / Google Calendar
- `IMcpToolClient` 인터페이스로 추상화 → `NotionMcpClient` / `MockMcpClient`
- 환경 변수 토큰 부재 시 자동 Mock 모드 (UI에 "(목 모드)" 표기)
- 저장 매핑: 일기 본문 → Notion 페이지 본문 / `confirmed` 일정만 Calendar 기본 동기화

---

## 5. 인터페이스 계약 (변경 금지)

### 5.1 fitness_routine (agent ↔ web 루틴 카드)
```json
{
  "type": "fitness_routine",
  "date": "2026-08-22",
  "target": "shoulders",
  "condition_summary": "왼쪽 어깨 결림, 머신 위주 선호",
  "exercises": [
    { "name": "머신 숄더 프레스", "sets": 2, "reps": 12, "rpe": 6, "rest_sec": 90, "is_warmup": false }
  ],
  "notes": "오버헤드 프레스 → 머신 숄더 프레스 대체 (피로 부위 저부하 전환). ...",
  "diary_snippet": "오늘은 어깨 결림을 고려해 회복 위주 어깨 운동을 진행했다...",
  "estimated_minutes": 21
}
```
- `is_warmup`, `estimated_minutes`는 optional 확장 필드
- web은 [RoutineCard](../src/web/src/components/RoutineCard.tsx)로 렌더, Writer는 `diary_snippet`을 일기에 삽입

### 5.2 데이터 모델 (api)
```typescript
interface DiaryEntry {
  date: string;                       // "2026-08-22" (PK)
  rawContent: string;
  enrichedContent: string;            // Markdown
  hashtags: string[];
  photos: Photo[];
  metadata: DiaryMetadata;            // workouts/meetings/studies/expenses/schedules
  createdAt: string;
}

interface Schedule {
  id: string; title: string; datetime: string;
  status: "confirmed" | "tentative";
  source: "today" | "tomorrow_plan";
  done: boolean;
}

interface DetectedSpan { start: number; end: number; type: "workout"|"meeting"|"study"|"expense"|"activity"; }
```

---

## 6. API 명세

### 6.1 DailyMate.Api
| Method | Endpoint | 설명 |
|---|---|---|
| GET/POST/PUT | `/api/diaries`, `/api/diaries/{date}` | 일기 CRUD (EF Core + SQLite) |
| POST | `/api/photos` · GET `/photos/{name}` | 사진 업로드(10MB·확장자 검증)/서빙 |
| GET/POST/PATCH | `/api/schedules` | 일정 등록·완료 토글 |
| GET | `/api/stats/weekly` | 주간 통계 |
| GET | `/health` `/alive` | 헬스체크 (전 서비스 공통, ServiceDefaults) |

### 6.2 DailyMate.Agent
| Method | Endpoint | 설명 |
|---|---|---|
| GET | `/agent/status` | 모드(copilot/mock)·MCP 연결 상태 — UI 투명성 |
| POST | `/agent/detect` | 활동 감지 `{ spans[] }` (결정적) |
| POST | `/agent/chat` | **SSE 스트리밍** 인터뷰 + 헬스 코치 핸드오프 |
| POST | `/agent/enrich` | 작가 에이전트 풍부화 `{ enrichedContent, hashtags[] }` |

### 6.3 DailyMate.McpTool
| Endpoint | 설명 |
|---|---|
| `/mcp` | MCP streamable HTTP (`get_exercises`/`calc_intensity`/`build_routine`) |

---

## 7. Azure 배포 설계 (필요한 리소스만)

| 리소스 | 용도 |
|---|---|
| Azure Container Apps ×4 | web / api / agent / mcp-tool |
| Azure Container Registry | 컨테이너 이미지 |
| Log Analytics | Aspire OTel 텔레메트리 수집 (관찰 가능성) |

> Azure AI/OpenAI는 사용하지 않는다 — LLM은 GitHub Copilot SDK로 연결 (심사 기준상 Azure AI 필수 아님).
> 형식적 리소스 추가 금지 원칙: 위 3종 외 서비스는 추가하지 않는다.

**반복 가능 배포** ([azure.yaml](../azure.yaml) — Aspire AppHost 통합):
```bash
azd auth login
azd init          # 환경명 입력
azd provision     # 인프라 생성 (계획·검증)
azd deploy        # AppHost 토폴로지 → Container Apps 변환·배포
```
- 배포 검증: 전 서비스 `/health` green + E2E 데모 시나리오 1회
- 배포 환경 변수: `GH_TOKEN`(Copilot 인증), `NOTION_MCP_TOKEN` 등 — Container Apps secrets로 주입

---

## 8. 로컬 개발 환경

```bash
# 요구: .NET 10 SDK, Node.js 20+, GitHub Copilot CLI(로그인 상태)
cd src/web && npm install && cd ../..
dotnet run --project DailyMate.AppHost   # 전체 기동 (대시보드 URL 콘솔 출력)
```

환경 변수 (없으면 Mock 모드 — 커밋 금지):
```
GH_TOKEN                  # 배포 환경 Copilot 인증 (로컬은 CLI 로그인 세션 자동 사용)
COPILOT_CLI_PATH          # CLI 경로 수동 지정 (PATH에 있으면 자동 감지)
NOTION_MCP_TOKEN / NOTION_DATABASE_ID
GOOGLE_CALENDAR_MCP_TOKEN
DAILYMATE_LLM=off         # LLM 강제 비활성화 (빠른 목 모드 데모)
```

---

## 9. 보안 · 책임 있는 AI

| 항목 | 구현 |
|---|---|
| 시크릿 관리 | 전부 환경 변수 주입, 저장소 하드코딩 0건 (푸시 전 시크릿 스캔 수행) |
| 사용자 확인 | 일정 등록·Notion 저장은 명시적 승인 후에만 실행 |
| 환각 완화 | 강도 계산·일정 파싱·감지는 결정적 코드/MCP 툴, LLM 출력은 JSON 스키마 강제+검증 후 사용 |
| 프롬프트 인젝션 대응 | 에이전트별 역할 바운더리(agents.md) + LLM 출력은 파싱 실패 시 폐기·폴백 |
| 의료 안전 | 의료 조언 금지, 통증 시 강도 하향 + 전문의 안내 |
| AI 표시 | 에이전트 응답 UI 구분, `/agent/status`로 LLM/Mock 모드 투명 공개 |

---

## 10. 기술적 리스크 및 대응

| 리스크 | 대응 |
|---|---|
| LLM 불가(네트워크·인증 장애) | Copilot CLI 미탐지 시 Mock 모드 자동 폴백 — 전체 플로우 유지 |
| MCP 서버 불안정 (시연 중 장애) | Mock 모드 자동 전환, 로컬 저장 유지 (데이터 유실 금지) |
| LLM 응답 지연 | SSE 스트리밍 + 감지 API 디바운스 + resilience 타임아웃 조정 |
| LLM 구조화 출력 불안정 | JSON 전용 인스트럭션 + 코드펜스 허용 파서 + 규칙 기반 트리아지 폴백 |
| 활동 감지 오프셋 오류 (한글) | UTF-16 코드유닛 기준 오프셋 통일 |
| 트래픽 이슈 | 서비스 4분리로 개별 scale-out, Container Apps 오토스케일 + 헬스체크 프로브 |
