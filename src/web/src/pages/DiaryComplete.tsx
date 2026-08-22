import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useDiaryStore } from '../store/diaryStore'
import type { DiaryMetadata } from '../types'

const DAYS = ['일', '월', '화', '수', '목', '금', '토']

/** 간단한 마크다운 볼드 렌더링 */
export function renderBold(text: string) {
  const parts = text.split(/(\*\*[^*]+\*\*)/g)
  return parts.map((p, i) =>
    p.startsWith('**') && p.endsWith('**') ? <b key={i}>{p.slice(2, -2)}</b> : <span key={i}>{p}</span>,
  )
}

export default function DiaryComplete() {
  const nav = useNavigate()
  const { date, rawContent, history, photos, enrichedContent, hashtags, setEnriched } = useDiaryStore()
  const [meta, setMeta] = useState<DiaryMetadata | null>(null)
  const [loading, setLoading] = useState(!enrichedContent)
  const [savedLocal, setSavedLocal] = useState(false)
  const [toast, setToast] = useState('')
  const startedRef = useRef(false)

  const showToast = (msg: string) => {
    setToast(msg)
    setTimeout(() => setToast(''), 3000)
  }

  const enrich = async () => {
    setLoading(true)
    try {
      const [e, m] = await Promise.all([api.enrich(rawContent, history), api.extract(rawContent, history)])
      setEnriched(e.enrichedContent, e.hashtags)
      setMeta(m)
    } catch {
      setEnriched(rawContent, [])
      showToast('꾸미기에 실패해 원문을 표시해요')
    }
    setLoading(false)
  }

  useEffect(() => {
    if (!rawContent.trim()) { nav('/write'); return }
    if (startedRef.current) return
    startedRef.current = true
    if (!enrichedContent) enrich()
    else api.extract(rawContent, history).then(setMeta).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const save = async () => {
    await api.saveDiary({
      date,
      rawContent,
      enrichedContent,
      hashtags,
      photos,
      metadata: meta ?? undefined,
      createdAt: '',
    })
    setSavedLocal(true)
    showToast('💾 일기가 저장됐어요!')
  }

  const saveNotion = async () => {
    if (!savedLocal) await save()
    const res = await api.mcpNotion({ date, content: enrichedContent })
    showToast(res.ok ? `📓 ${res.message}` : `⚠️ ${res.message}`)
  }

  const syncCalendar = async () => {
    const schedules = meta?.schedules ?? []
    const res = await api.mcpCalendar({ schedules })
    showToast(res.ok ? `📅 ${res.message}` : `⚠️ ${res.message}`)
  }

  const d = new Date(date)
  const title = `${d.getMonth() + 1}월 ${d.getDate()}일 ${DAYS[d.getDay()]}요일`
  const confirmedCount = meta?.schedules.filter((s) => s.status === 'confirmed').length ?? 0
  const tentativeCount = meta?.schedules.filter((s) => s.status === 'tentative').length ?? 0

  return (
    <>
      <div className="header">
        <span><span className="back" onClick={() => nav('/chat')}>←</span> 오늘의 일기 완성</span>
        <span className="sub">{date.replaceAll('-', '.')}</span>
      </div>
      <div className="body">
        <div className="msg agent" style={{ maxWidth: '100%' }}>
          🤖 {loading ? '대화 내용으로 일기를 꾸미는 중이에요…' : '대화 내용으로 일기를 더 풍부하게 꾸몄어요.'}
        </div>

        <div className="diary-page">
          <div className="diary-title">{title} 일기</div>
          <div className="diary-text">{loading ? '…' : renderBold(enrichedContent)}</div>
          {photos.length > 0 && (
            <div className="photo-strip">
              {photos.map((p) => (
                <div key={p.id} className="photo"><img src={p.filename} alt={p.caption ?? '첨부 사진'} /></div>
              ))}
            </div>
          )}
          {photos.some((p) => p.linkedTopic) && (
            <div style={{ fontSize: 11, color: 'var(--ink-faint)', marginTop: 4 }}>
              대화 중 첨부한 사진은 해당 주제에 연결돼 함께 저장돼요.
            </div>
          )}
          {hashtags.length > 0 && <div className="tags">{hashtags.join(' ')}</div>}
        </div>

        <div className="ai-note">
          <span className="pen-mark">파란 밑줄</span>은 대화 내용을 바탕으로 AI가 덧쓴 문장이에요. 사실과 다르면 다시 꾸미거나 직접 고칠 수 있어요.
        </div>

        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn ghost small" style={{ flex: 1 }} onClick={enrich} disabled={loading}>다시 꾸미기</button>
          <button className="btn small" style={{ flex: 1 }} onClick={save} disabled={loading}>일기 저장</button>
        </div>

        {meta && (
          <div className="card">
            <div className="section-title">함께 저장되는 기록</div>
            {meta.workouts.map((w, i) => (
              <div key={`w${i}`} className="extract-item">💪 운동: {w.exercise}{w.weight ? ` ${w.weight}` : ''}{w.sets ? ` ${w.sets}` : ''}</div>
            ))}
            {meta.meetings.map((m, i) => <div key={`m${i}`} className="extract-item">📝 회의록: {m.title}</div>)}
            {meta.studies.map((s, i) => <div key={`s${i}`} className="extract-item">📚 학습: {s.topic}</div>)}
            {meta.expenses.map((e, i) => <div key={`e${i}`} className="extract-item">💸 지출: {e.item}</div>)}
            {(confirmedCount > 0 || tentativeCount > 0) && (
              <div className="extract-item">📅 내일 일정 {confirmedCount}건 확정 · {tentativeCount}건 미정</div>
            )}
            <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
              <button className="btn small" style={{ flex: 1 }} onClick={saveNotion}>Notion에 저장</button>
              <button className="btn small ghost" style={{ flex: 1 }} onClick={syncCalendar}>캘린더와 동기화</button>
            </div>
          </div>
        )}

        <button className="btn gray" onClick={() => nav('/')}>홈으로 돌아가기</button>
      </div>
      {toast && <div className="toast">{toast}</div>}
    </>
  )
}
