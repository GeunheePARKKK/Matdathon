import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import TabBar from '../components/TabBar'
import { todayStr } from '../store/diaryStore'
import type { DiaryEntry, Schedule, WeeklyStats } from '../types'

const DAYS = ['일', '월', '화', '수', '목', '금', '토']

function greeting(): string {
  const h = new Date().getHours()
  if (h < 6) return '늦은 밤이에요'
  if (h < 12) return '좋은 아침이에요'
  if (h < 18) return '좋은 오후예요'
  return '좋은 저녁이에요'
}

export default function Home() {
  const nav = useNavigate()
  const [schedules, setSchedules] = useState<Schedule[]>([])
  const [stats, setStats] = useState<WeeklyStats | null>(null)
  const [diaries, setDiaries] = useState<DiaryEntry[]>([])
  const today = todayStr()
  const d = new Date()

  useEffect(() => {
    api.schedules(today).then(setSchedules).catch(() => {})
    api.weeklyStats().then(setStats).catch(() => {})
    api.diaries().then(setDiaries).catch(() => {})
  }, [today])

  const toggle = async (s: Schedule) => {
    const updated = await api.toggleSchedule(s.id, !s.done)
    setSchedules((prev) => prev.map((x) => (x.id === s.id ? updated : x)))
  }

  const remove = async (s: Schedule) => {
    await api.deleteSchedule(s.id)
    setSchedules((prev) => prev.filter((x) => x.id !== s.id))
  }

  return (
    <>
      <div className="header">
        🌙 DailyMate
        <span className="sub">
          {d.getFullYear()}.{String(d.getMonth() + 1).padStart(2, '0')}.{String(d.getDate()).padStart(2, '0')} ({DAYS[d.getDay()]})
        </span>
      </div>
      <div className="body">
        <div className="hero-night">
          <span className="moon" aria-hidden="true" />
          <div className="greet">{greeting()}</div>
          <div className="invite">오늘 하루를 한 장의 일기로 남겨볼까요?</div>
          <button className="btn" style={{ background: '#F4F2EA', color: '#232C43' }} onClick={() => nav('/write')}>오늘 일기 쓰기</button>
        </div>

        <div>
          <div className="section-title">오늘 하기로 했던 일</div>
          <div className="card solid">
            {schedules.length === 0 && <div className="empty">등록된 일정이 없어요</div>}
            {schedules.map((s) => (
              <div key={s.id} className={`schedule-item${s.done ? ' done' : ''}`}>
                <input type="checkbox" checked={s.done} onChange={() => toggle(s)} aria-label={`${s.title} 완료`} />
                <span className="grow">
                  {s.datetime.slice(11, 16)} {s.title}
                </span>
                {s.status === 'tentative' && <span className="badge gray">미정</span>}
                <button className="icon-btn" aria-label={`${s.title} 일정 삭제`} onClick={() => remove(s)}>✕</button>
              </div>
            ))}
          </div>
        </div>

        <div>
          <div className="section-title">이번 주 기록</div>
          <div className="stat-row">
            <div className="stat"><div className="emoji">📖</div><div className="label">일기</div><div className="value">{stats?.diaryDays ?? 0}일</div></div>
            <div className="stat"><div className="emoji">📝</div><div className="label">회의록</div><div className="value">{stats?.meetings ?? 0}건</div></div>
            <div className="stat"><div className="emoji">📅</div><div className="label">등록 일정</div><div className="value">{stats?.schedules ?? 0}건</div></div>
          </div>
        </div>

        <div>
          <div className="section-title">최근 일기</div>
          <div className="card solid">
            {diaries.length === 0 && <div className="empty">아직 작성한 일기가 없어요</div>}
            {diaries.slice(0, 5).map((e) => (
              <div key={e.date} className="list-item" onClick={() => nav(`/diary/${e.date}`)}>
                <span>
                  {e.date.slice(5).replace('-', '.')} "{e.rawContent.slice(0, 16)}{e.rawContent.length > 16 ? '…' : ''}"
                  {e.photos.length > 0 && ` 📷${e.photos.length}`}
                </span>
                <span>→</span>
              </div>
            ))}
          </div>
        </div>
      </div>
      <TabBar />
    </>
  )
}
