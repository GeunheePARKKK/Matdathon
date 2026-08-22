# 🌙 DailyMate — Copilot CLI 작업 브리핑

> 이 문서는 GitHub Copilot CLI가 프로젝트 전체 맥락을 이해하기 위한 단일 진실 소스(SSOT)다.
> 모든 작업 전에 이 문서를 먼저 읽을 것.

---

## 0. 해커톤 규칙 (절대 준수)

- **마감: 오늘 16:30 제출. 넘기면 자동 탈락.** 모든 판단은 마감 역산으로.
- 코딩 도구: **GitHub Copilot만 사용** (VS Code Copilot / Copilot CLI / Copilot app). 타사 AI 도구 금지.
- **Azure 클라우드 배포 필수** — 제출물에 배포된 앱 URL 필요.
- **로그인/인증 기능 절대 금지** (로컬 세션 기반으로 동작).
- 필수 기술: **웹 앱(React)** + **Copilot SDK** + **Microsoft Agent Framework** + **MCP** + **.NET Aspire** + **Azure 배포**.
- `PRD.md` / `TRD.md`는 AI가 심사함 — 문서 완성도 중요.
- ai-slop 피하기, 오버엔지니어링 금지, 코드 간결화는 후반에.

---

## 1. 앱 컨셉 (와이어프레임 v2 기준 — 최신)

**DailyMate**: 사용자가 일기를 직접 쓰면, 에이전트가 일기 속 활동(운동·회의·공부 등)을
실시간 감지하고, 작성 완료 후 주제별 심화 질문을 던져 일기를 풍부하게 완성한다.
구조화 데이터(운동 기록·회의록·학습·내일 일정)를 추출해 MCP로 Notion/Calendar에 저장한다.

### 사용자 플로우
```
① 일기 직접 작성 (에디터에서 자유 서술)
② 에이전트가 문맥 감지 (운동/회의/공부 하이라이트)
③ 주제별 심화 질문 (챗 UI)
   - 운동 감지 시 → 💪 헬스 코치 에이전트가 질문
     ("무슨 운동? 중량은?") + 운동 기록 저장
     + 내일 운동 계획 언급 시 → 컨디션 맞춤 루틴 생성
④ 내일 계획 질문 → 일정 등록
⑤ 일기 풍부화 완성 (답변 내용 본문에 보강, 사진 자동 배치, 해시태그)
⑥ MCP 저장 (Notion / Google Calendar)
```

### 화면 4개
1. **홈 대시보드** — 오늘 하기로 했던 일(체크), 이번 주 기록 통계, 최근 일기
2. **일기 작성 에디터** — 자유 서술 + 실시간 활동 감지 하이라이트
3. **에이전트 심화 질문 챗** — 일기 문장 인용 + 질문, 답변 수집, 내일 일정 정리 카드
4. **완성된 일기** — 보강된 본문 + 사진 배치 + 해시태그 + MCP 저장 버튼

---

## 2. 아키텍처

```
React Web UI
   │
   ▼
API 서비스 (.NET Minimal API)
   │
   ▼
Agent 서비스 (MS Agent Framework + Copilot SDK)
   ├─ 감지(트리아지) 에이전트: 일기 텍스트에서 활동 주제 추출
   ├─ 심화질문 에이전트: 주제별 질문 생성·답변 수집
   ├─ 💪 헬스 코치 에이전트: 운동 기록 파싱 + 컨디션 맞춤 루틴 생성
   └─ 작가 에이전트: 답변 반영해 일기 본문 풍부화
   │
   ▼
MCP Tool 서버
   ├─ 운동 DB 조회 / 강도 계산 / 루틴 생성  ← 헬스 파트
   └─ Notion 저장 / Calendar 등록 / 내보내기
   │
Aspire AppHost가 전체 오케스트레이션 (서비스 디스커버리·텔레메트리·헬스체크)
   │
   ▼
Azure Container Apps 배포
```

### Aspire 서비스 구성
- `web` — React UI
- `api` — REST API, 세션(무로그인)/일기 저장
- `agent` — 에이전트 4종 실행
- `mcp-tool` — MCP 툴 서버

---

## 3. 헬스 파트 상세 (현재 최우선 작업)

### MCP 툴 3개 (C#, ModelContextProtocol NuGet)
| 툴 | 입력 | 출력 |
|---|---|---|
| `get_exercises` | target(부위), equipment? | 운동 후보 리스트 (시드 JSON) |
| `calc_intensity` | fatigue_level, pain_areas[], heavy_preference | 볼륨 배율, RPE 상한, 제외 패턴 |
| `build_routine` | 운동 후보 + 강도 파라미터 | fitness_routine JSON |

### 강도 계산 룰
- heavy_preference=false → RPE 상한 7, 머신/케이블 우선
- 인접 부위 피로 → joint_load 겹치는 운동 제외, alternatives로 대체
- pain_areas 포함 → 해당 부위 운동 전부 제외 + notes 경고
- fatigue high → 세트 수 -25%, 보조운동 1개 축소
- **의료 조언 금지. 통증 호소 시 강도 하향 + 병원 안내 문구.**

### 운동 시드 DB (Data/exercises.json)
부위별 5~8개 제한 (chest/back/shoulders/legs). 각 운동:
`name, type(compound|isolation), equipment, joint_load[], default{sets,reps,rpe,rest_sec}, alternatives[]`

### 인터페이스 계약 (팀원과 합의된 스키마 — 변경 금지)
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
→ UI는 루틴 카드로 렌더, 작가 에이전트는 diary_snippet을 일기에 삽입.

---

## 4. 역할 분담

| 파트 | 담당 |
|---|---|
| 감지·심화질문·작가 에이전트 + 일기 UI | 팀원 |
| **헬스 코치 에이전트 + 운동 MCP 툴 + 루틴 카드 UI** | **나 (이 세션의 주 작업)** |
| Aspire AppHost + Azure 배포 | 공동 |

---

## 5. 개발 순서 (마감 역산)

1. **Phase 1**: Aspire AppHost + web/api/agent/mcp-tool 스캐폴딩 + 채팅 왕복 1회 동작
2. **★ 1차 Azure 배포** (동작하는 뼈대 상태에서 URL 확보 — 배포 이슈 조기 발견)
3. **Phase 2 (병렬)**: 나=헬스 MCP 툴+코치 에이전트 / 팀원=감지·심화질문 에이전트
4. **Phase 3**: 통합 (감지→핸드오프→작가 에이전트 풍부화)
5. **★ 2차 배포** → **Phase 4**: 문서(PRD/TRD/agents.md) 완성 + 폴리싱
6. **★ 최종 배포 + 16:00까지 제출 완료 목표 (30분 버퍼)**

---

## 6. 하지 말 것

- 로그인/회원가입/인증
- 복잡한 DB (파일 기반 or 인메모리로 충분)
- 통계 화면 고도화, 애니메이션, 자동화 테스트 과투자
- 운동 DB 확장 (부위 4개 × 5~8개면 충분, 데모는 chest 중심)
- 스키마 임의 변경 (팀원과 계약 깨짐)
```