import './RoutineCard.css'

/** TRD §5 fitness_routine 계약 스키마 (is_warmup / estimated_minutes는 optional 확장) */
export interface RoutineExercise {
  name: string
  sets: number
  reps: number
  rpe: number
  rest_sec: number
  is_warmup?: boolean
}

export interface FitnessRoutine {
  type: 'fitness_routine'
  date: string
  target: string
  condition_summary: string
  exercises: RoutineExercise[]
  notes?: string
  diary_snippet?: string
  estimated_minutes?: number
}

const TARGET_KO: Record<string, string> = {
  chest: '가슴',
  back: '등',
  shoulders: '어깨',
  legs: '하체',
  core: '코어',
}

/** data 페이로드가 fitness_routine인지 판별하는 타입 가드 */
export function isFitnessRoutine(data: unknown): data is FitnessRoutine {
  return (
    typeof data === 'object' &&
    data !== null &&
    (data as { type?: unknown }).type === 'fitness_routine' &&
    Array.isArray((data as { exercises?: unknown }).exercises)
  )
}

/** 헬스 코치 fitness_routine을 카드로 렌더링하는 독립 컴포넌트 */
export default function RoutineCard({ routine }: { routine: FitnessRoutine }) {
  const targetLabel = TARGET_KO[routine.target] ?? routine.target
  return (
    <div className="card solid routine-card">
      <div className="routine-card__header">
        <h3 className="routine-card__title">
          💪 오늘의 {targetLabel} 루틴
        </h3>
        <span className="routine-card__date">{routine.date}</span>
      </div>

      <div className="routine-card__meta">
        <span className="badge">{routine.condition_summary}</span>
        {routine.estimated_minutes != null && (
          <span className="badge badge--time">⏱️ 약 {routine.estimated_minutes}분</span>
        )}
      </div>

      <ul className="routine-card__list">
        {routine.exercises.map((ex, i) => (
          <li
            key={i}
            className={`routine-card__item${ex.is_warmup ? ' routine-card__item--warmup' : ''}`}
          >
            <span className="routine-card__name">
              {ex.is_warmup && <span className="badge badge--warmup">🔥 워밍업</span>}
              {ex.name}
            </span>
            <span className="routine-card__detail">
              {ex.sets}세트 × {ex.reps}회 · RPE {ex.rpe} · 휴식 {ex.rest_sec}초
            </span>
          </li>
        ))}
      </ul>

      {routine.notes && (
        <div className="routine-card__notes">
          <span className="routine-card__notes-icon">⚠️</span>
          <p>{routine.notes}</p>
        </div>
      )}
    </div>
  )
}
