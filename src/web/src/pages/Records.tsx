import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import TabBar from '../components/TabBar'
import type { DiaryEntry, Schedule } from '../types'

export default function Records() {
  const nav = useNavigate()
  const [diaries, setDiaries] = useState<DiaryEntry[]>([])
  const [schedules, setSchedules] = useState<Schedule[]>([])

  useEffect(() => {
    api.diaries().then(setDiaries).catch(() => {})
    api.schedules().then(setSchedules).catch(() => {})
  }, [])

  const removeDiary = async (date: string) => {
    if (!confirm(`${date} 일기를 삭제할까요? 되돌릴 수 없어요.`)) return
    await api.deleteDiary(date)
    setDiaries((prev) => prev.filter((d) => d.date !== date))
  }

  const removeSchedule = async (id: string) => {
    await api.deleteSchedule(id)
    setSchedules((prev) => prev.filter((s) => s.id !== id))
  }

  return (
    <>
      <div className="header">기록<span className="sub">일기 · 일정 모아보기</span></div>
      <div className="body">
        <div>
          <div className="section-title">일기 ({diaries.length})</div>
          <div className="card solid">
            {diaries.length === 0 && <div className="empty">아직 작성한 일기가 없어요</div>}
            {diaries.map((e) => (
              <div key={e.date} className="list-item" onClick={() => nav(`/diary/${e.date}`)}>
                <span>{e.date} "{e.rawContent.slice(0, 16)}{e.rawContent.length > 16 ? '…' : ''}"</span>
                <button
                  className="icon-btn"
                  aria-label={`${e.date} 일기 삭제`}
                  onClick={(ev) => { ev.stopPropagation(); removeDiary(e.date) }}
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        </div>
        <div>
          <div className="section-title">등록된 일정 ({schedules.length})</div>
          <div className="card solid">
            {schedules.length === 0 && <div className="empty">등록된 일정이 없어요</div>}
            {schedules.map((s) => (
              <div key={s.id} className={`schedule-item${s.done ? ' done' : ''}`}>
                <span>{s.done ? '✅' : '☐'}</span>
                <span className="grow">{s.datetime.slice(0, 16).replace('T', ' ')} {s.title}</span>
                {s.status === 'tentative' && <span className="badge gray">미정</span>}
                <button className="icon-btn" aria-label={`${s.title} 일정 삭제`} onClick={() => removeSchedule(s.id)}>✕</button>
              </div>
            ))}
          </div>
        </div>
      </div>
      <TabBar />
    </>
  )
}
