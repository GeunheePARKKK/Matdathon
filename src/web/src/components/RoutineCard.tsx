import type { FitnessRoutine } from '../types'

const TARGET_KO: Record<string, string> = {
  chest: '가슴', back: '등', shoulders: '어깨', legs: '하체', core: '코어',
}

/** 헬스 코치 루틴 카드 — 밤의 일기장 스타일 */
export default function RoutineCard({ routine }: { routine: FitnessRoutine }) {
  const target = TARGET_KO[routine.target] ?? routine.target
  return (
    <div className="card solid" style={{ marginTop: 8 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 6 }}>
        <span style={{ fontFamily: 'var(--serif)', fontWeight: 700, fontSize: 15 }}>💪 오늘의 {target} 루틴</span>
        <span style={{ fontSize: 11.5, color: 'var(--ink-faint)' }}>
          {routine.date}{routine.estimated_minutes ? ` · 약 ${routine.estimated_minutes}분` : ''}
        </span>
      </div>
      <div style={{ marginBottom: 6 }}>
        <span className="badge blue">{routine.condition_summary}</span>
      </div>
      {routine.exercises.map((ex, i) => (
        <div key={i} className="schedule-item">
          <span className="grow">
            {ex.is_warmup && <span className="badge red" style={{ marginRight: 6 }}>워밍업</span>}
            {ex.name}
          </span>
          <span style={{ fontSize: 12, color: 'var(--ink-soft)', whiteSpace: 'nowrap' }}>
            {ex.sets}세트 × {ex.reps}회 · RPE {ex.rpe} · 휴식 {ex.rest_sec}초
          </span>
        </div>
      ))}
      {routine.notes && (
        <div className="enrich-note" style={{ marginTop: 8 }}>{routine.notes}</div>
      )}
    </div>
  )
}
