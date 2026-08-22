# TRD — DailyMate 🌙

## 1. 기술 스택 요약

| Layer | 기술 | 근거 |
|---|---|---|
| Frontend | React (Vite) + TypeScript | 해커톤 필수(웹 앱), 빠른 스캐폴딩 |
| API | .NET 9 Minimal API | 경량, Aspire 통합 용이 |
| AI/LLM | **Microsoft Agent Framework** + **Copilot SDK**, Azure OpenAI | 해커톤 필수, 멀티 에이전트 핸드오프 지원 |
| Tools | **MCP** (C#, ModelContextProtocol) | 해커톤 필수, 결정적 계산의 툴 분리 |
| Infra | **.NET Aspire** | 서비스 오케스트레이션·디스커버리·OTel·헬스체크 |
| Deploy | **Azure Container Apps** (azd) | 해커톤 필수, Aspire 네이티브 지원 |

## 2. 아키텍처

```
React Web UI ──► API (.NET Minimal API) ──► Agent 서비스 (MAF + Copilot SDK)
                                              ├─ 감지(트리아지) 에이전트
                                              ├─ 심화질문 에이전트
                                              ├─ 헬스 코치 에이전트 ──► MCP Tool 서버
                                              └─ 작가 에이전트            ├─ 운동 DB / 강도계산 / 루틴 생성
                                                                          └─ Notion / Calendar / 내보내기
[.NET Aspire AppHost] — 전 서비스 등록·디스커버리·OpenTelemetry·헬스체크
[Azure Container Apps] — 배포 대상
```

### Aspire 서비스 구성
| 서비스 | 역할 |
|---|---|
| `web` | React UI (Vite) |
| `api` | REST API, 세션(무로그인)·일기 저장(인메모리/파일) |
| `agent` | 에이전트 4종 실행 |
| `mcp-tool` | MCP 툴 서버 |

- 서비스 간 연결은 Aspire 서비스 디스커버리로 자동 해석 (하드코딩 URL 금지)
- 분산 트래픽 대응: 서비스 분리로 개별 scale-out 가능, OTel로 병목 추적

## 3. 에이전트 설계 (Microsoft Agent Framework)

핸드오프 패턴: 트리아지 에이전트가 의도 분류 후 전문 에이전트에 위임.

| 에이전트 | 입력 | 출력 |
|---|---|---|
| 감지 | 일기 원문 | 주제 목록 `[{type, quote, span}]` |
| 심화질문 | 주제 + 대화 이력 | 질문 텍스트 / 추출 데이터 |
| 헬스 코치 | 운동 발화 + 컨디션 | `fitness_routine` JSON (§5 계약) |
| 작가 | 일기 원문 + 추출 데이터 | 풍부화된 일기 MD + 해시태그 |

## 4. MCP 툴 명세 (헬스 파트)

| 툴 | 입력 | 출력 |
|---|---|---|
| `get_exercises` | `target`, `equipment?` | 운동 후보 목록 (시드 JSON) |
| `calc_intensity` | `fatigue_level`, `pain_areas[]`, `heavy_preference` | `volume_multiplier`, `rpe_cap`, `excluded_joint_loads[]` |
| `build_routine` | 후보 + 강도 파라미터 | `fitness_routine` JSON |

강도 룰: heavy_preference=false → RPE≤7·머신 우선 / 피로 부위 joint_load 겹침 → 제외·대체 / 통증 → 전면 제외+경고 / fatigue high → 볼륨 −25%.

## 5. 인터페이스 계약 (변경 금지)

```json
{
  "type": "fitness_routine",
  "date": "2026-08-22",
  "target": "chest",
  "condition_summary": "전면 어깨 피로, 고중량 비선호",
  "exercises": [
    { "name": "머신 체스트 프레스", "sets": 3, "reps": 12, "rpe": 7, "rest_sec": 90 }
  ],
  "notes": "어깨 전면 통증 시 가동범위 축소",
  "diary_snippet": "오늘은 회복 위주 가슴 운동을 진행했다..."
}
```

## 6. 데이터 모델

```
일기 엔트리 (파일/인메모리)
├─ diary.md        ← 본문 (운동 스니펫 포함)
└─ metadata.json
    ├─ workouts[]  { date, target, condition_summary, exercises[], notes }
    ├─ meetings[]  { title, notes, photos[] }
    ├─ studies[]   { topic, notes }
    └─ schedules[] { title, datetime, confirmed }
```

## 7. API 엔드포인트 (최소)

| Method | Path | 역할 |
|---|---|---|
| POST | `/api/diary` | 일기 저장(자동저장) |
| POST | `/api/chat` | 에이전트 대화 (감지/질문/코치/작가 라우팅) |
| GET | `/api/diary/today` | 오늘 일기+메타데이터 조회 |
| GET | `/health` | 헬스체크 (전 서비스) |

## 8. 배포 (Azure)

1. `azd init` → Aspire AppHost 기반 인프라 생성
2. `azd up` → Container Apps 배포
3. 환경변수: Azure OpenAI 엔드포인트/키 (Secrets로 관리, 코드 하드코딩 금지)
4. 배포 검증: `/health` 전 서비스 green + E2E 데모 시나리오 1회

## 9. 리스크 및 완화

| 리스크 | 완화 |
|---|---|
| Azure 배포 이슈로 마감 초과 | Phase 1.5에서 조기 1차 배포로 이슈 선제거 |
| LLM 구조화 출력 불안정 | MCP 툴이 결정적 계산 담당, LLM은 해석만 |
| 팀 병렬 작업 충돌 | §5 계약 스키마 고정, 서비스 경계로 분리 |