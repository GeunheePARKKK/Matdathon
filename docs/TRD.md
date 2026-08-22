# 🛠 TRD — DailyMate (기술 요구사항 문서)

> Technical Requirements Document
> 버전: 0.1 (초안) · 작성일: 2026-08-22

---

## 1. 기술 스택

| 계층 | 기술 | 비고 |
|---|---|---|
| Frontend | React 18 + TypeScript + Vite | 상태관리: Zustand (최소화) |
| Backend API | ASP.NET Core (.NET 8) Minimal API | REST |
| AI/LLM Layer | **Microsoft Agent Framework** + **GitHub Copilot SDK** | 해커톤 필수 |
| Tool Layer | **MCP** (Notion, Google Calendar) | 목 모드 지원 필수 |
| Infra Layer | **.NET Aspire** | 오케스트레이션·디스커버리·텔레메트리·헬스체크 |
| 저장소 | 로컬: SQLite / 배포: Azure Storage (Blob + Table) | 로그인 없음 → 세션 키 기반 |
| 배포 | Azure Container Apps (azd) | GitHub → Azure |

---

## 2. 시스템 아키텍처

```
┌─────────────────────────────────────────────────────┐
│              .NET Aspire AppHost                     │
│  (서비스 디스커버리 · OpenTelemetry · 헬스체크)       │
└─────────────────────────────────────────────────────┘
        │                │                 │
        ▼                ▼                 ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────────┐
│  web         │  │  api         │  │  agent           │
│  React+Vite  │─▶│  ASP.NET     │─▶│  MS Agent Fx     │
│  (UI)        │  │  Minimal API │  │  + Copilot SDK   │
└──────────────┘  └──────┬───────┘  └────────┬─────────┘
                         │                   │
                         ▼                   ▼
                  ┌────────────┐    ┌─────────────────┐
                  │ SQLite /   │    │  MCP Tools      │
                  │ Azure      │    │  · Notion       │
                  │ Storage    │    │  · G. Calendar  │
                  └────────────┘    └─────────────────┘
```

### 서비스 간 통신
- `web → api`: REST (Aspire 서비스 디스커버리로 URL 자동 주입)
- `api → agent`: HTTP 내부 호출
- `agent → LLM`: Copilot SDK / Azure OpenAI
- `agent → 외부`: MCP 클라이언트 (stdio 또는 HTTP transport)

---

## 3. 프로젝트 구조

```
dailymate/
├── DailyMate.AppHost/            # Aspire 진입점 — 전체 앱 구조를 코드로 정의
│   └── Program.cs                #   AddProject(api, agent) + AddNpmApp(web)
├── DailyMate.ServiceDefaults/    # 공통: OTel, 헬스체크, 리질리언스
├── src/
│   ├── web/
│   │   └── src/
│   │       ├── pages/            # Home / DiaryWrite / AgentChat / DiaryComplete / Integrations
│   │       ├── components/       # TabBar, HighlightEditor, ChatBubble, ScheduleCard ...
│   │       ├── api/              # fetch 클라이언트 (SSE 포함)
│   │       └── store/            # diaryStore, chatStore
│   ├── DailyMate.Api/
│   │   ├── Endpoints/            # Diaries, Schedules, Photos, Stats, Export
│   │   └── Data/                 # EF Core + SQLite
│   └── DailyMate.Agent/
│       ├── Agents/               # DetectorAgent, InterviewerAgent, WriterAgent
│       ├── Mcp/                  # NotionMcpClient, CalendarMcpClient, MockMcpClient
│       └── Endpoints/            # /agent/detect, /chat, /extract, /enrich, /schedule-parse
├── docs/                         # PRD.md, TRD.md, ideation.md, wireframe.html
└── agents.md
```

---

## 4. API 명세

### 4.1 DailyMate.Api

| Method | Endpoint | Request | Response |
|---|---|---|---|
| GET | `/api/diaries` | — | `DiaryEntry[]` |
| GET | `/api/diaries/{date}` | — | `DiaryEntry` |
| POST | `/api/diaries` | `DiaryEntry` | `201` |
| PUT | `/api/diaries/{date}` | `DiaryEntry` | `200` |
| POST | `/api/photos` | multipart | `Photo` |
| GET | `/api/schedules?date=` | — | `Schedule[]` |
| POST | `/api/schedules` | `Schedule[]` | `201` |
| PATCH | `/api/schedules/{id}` | `{ done }` | `200` |
| GET | `/api/stats/weekly` | — | `{ diaryDays, meetings, schedules }` |
| POST | `/api/export` | `{ date, format }` | 파일 스트림 |

### 4.2 DailyMate.Agent

| Method | Endpoint | Request | Response |
|---|---|---|---|
| POST | `/agent/detect` | `{ text }` | `{ spans: DetectedSpan[] }` |
| POST | `/agent/chat` | `{ diaryText, history[] }` | SSE 스트림 |
| POST | `/agent/extract` | `{ history[] }` | `DiaryMetadata` |
| POST | `/agent/enrich` | `{ rawContent, metadata, photos[] }` | `{ enrichedContent, hashtags[] }` |
| POST | `/agent/schedule-parse` | `{ text }` | `Schedule[]` (status 포함) |

---

## 5. 데이터 모델

```typescript
interface DiaryEntry {
  date: string;                       // "2026-08-22" (PK)
  rawContent: string;
  enrichedContent: string;            // Markdown
  hashtags: string[];
  photos: Photo[];
  metadata: DiaryMetadata;
  createdAt: string;
}

interface DiaryMetadata {
  workouts:  { exercise: string; weight?: string; sets?: string; note?: string }[];
  meetings:  { title: string; notes: string; photoIds: string[] }[];
  studies:   { topic: string; detail: string }[];
  expenses:  { item: string; amount: number; category: string }[];
  schedules: Schedule[];
}

interface Schedule {
  id: string;
  title: string;
  datetime: string;                   // ISO 8601
  status: "confirmed" | "tentative";
  source: "today" | "tomorrow_plan";
  done: boolean;
}

interface Photo {
  id: string;
  filename: string;
  caption?: string;
  linkedTopic?: "workout" | "meeting" | "study" | "expense" | "activity";
}

interface DetectedSpan {
  start: number;                      // rawContent 내 오프셋
  end: number;
  type: "workout" | "meeting" | "study" | "expense" | "activity";
}
```

---

## 6. 에이전트 기술 설계

### 6.1 구성 (Microsoft Agent Framework)
- 3개 에이전트를 **역할별로 분리**: Detector / Interviewer / Writer
- 각 에이전트는 독립 인스트럭션 + 독립 tool 목록 보유 (agents.md 참조)
- Interviewer → Writer 핸드오프 시 `DiaryMetadata`를 컨텍스트로 전달

### 6.2 MCP 통합
- `IMcpToolClient` 인터페이스로 추상화 → `NotionMcpClient` / `CalendarMcpClient` / `MockMcpClient`
- 환경 변수 토큰 부재 시 자동으로 Mock 모드 전환 (시연 안전장치)
- Notion 저장 매핑: 일기 본문 → 페이지 본문 / metadata → DB 속성

### 6.3 스트리밍
- `/agent/chat`은 SSE로 토큰 단위 스트리밍
- 프론트: `EventSource` 또는 `fetch` + ReadableStream

---

## 7. Azure 배포 설계

| 리소스 | 용도 |
|---|---|
| Azure Container Apps ×3 | web / api / agent |
| Azure Container Registry | 컨테이너 이미지 |
| Azure Storage (Blob) | 사진 저장 |
| Azure Storage (Table) 또는 SQLite 볼륨 | 일기·일정 데이터 |
| Azure OpenAI | LLM (Copilot SDK 백엔드) |
| Application Insights | 텔레메트리 (Aspire OTel 연동) |

배포 절차: `azd init` → **배포 계획 → 배포 검증 → 배포 실행** (Azure skills 순서 준수)

---

## 8. 로컬 개발 환경

```bash
# 요구: .NET 8 SDK, Node.js 20+, VS Code (C# Dev Kit, Aspire 확장)
dotnet workload install aspire
cd src/web && npm install
dotnet run --project DailyMate.AppHost   # 전체 기동
```

환경 변수 (user-secrets / .env — 커밋 금지):
```
AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY
NOTION_MCP_TOKEN          # 없으면 Mock 모드
GOOGLE_CALENDAR_MCP_TOKEN # 없으면 Mock 모드
```

---

## 9. 기술적 리스크 및 대응

| 리스크 | 대응 |
|---|---|
| MCP 서버 불안정 (시연 중 장애) | Mock 모드 자동 전환, UI는 정상 흐름 유지 |
| LLM 응답 지연 | SSE 스트리밍 + 감지 API 디바운스 |
| 활동 감지 오프셋 오류 (한글) | UTF-16 코드유닛 기준 오프셋 통일, 테스트 케이스 확보 |
| 오버엔지니어링 | 상태관리 최소화, 문체/질문 수 제한, Phase 6에서 코드 간결화 |
| 트래픽 이슈 | Container Apps 오토스케일 + Aspire 헬스체크 |