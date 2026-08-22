import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, streamChat } from '../api/client'
import RoutineCard from '../components/RoutineCard'
import { useDiaryStore } from '../store/diaryStore'
import type { ChatMessage, Schedule } from '../types'

type Phase = 'topic' | 'tomorrow' | 'schedule_preview' | 'finished' | 'loading'

export default function AgentChat() {
  const nav = useNavigate()
  const { rawContent, history, setHistory, addPhotos } = useDiaryStore()
  const [messages, setMessages] = useState<ChatMessage[]>(history)
  const [streaming, setStreaming] = useState('')
  const [quote, setQuote] = useState('')
  const [input, setInput] = useState('')
  const [phase, setPhase] = useState<Phase>('loading')
  const prevPhaseRef = useRef<Phase>('loading')
  const [preview, setPreview] = useState<Schedule[]>([])
  const [checked, setChecked] = useState<Record<string, boolean>>({})
  const [registered, setRegistered] = useState(false)
  const [currentTopic, setCurrentTopic] = useState<string | undefined>(undefined)
  const [toast, setToast] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const startedRef = useRef(false)

  const scrollDown = () => setTimeout(() => bottomRef.current?.scrollIntoView({ behavior: 'smooth' }), 50)

  const attachPhotos = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    try {
      const uploaded = await api.uploadPhotos(files, currentTopic)
      addPhotos(uploaded)
      const note: ChatMessage = { role: 'user', content: `📷 사진 ${uploaded.length}장 첨부${currentTopic ? ` (${currentTopic} 주제에 연결)` : ''}` }
      setMessages((prev) => [...prev, note])
      scrollDown()
    } catch {
      setToast('사진 업로드에 실패했어요. jpg/png 10MB 이하만 올릴 수 있어요.')
      setTimeout(() => setToast(''), 3000)
    }
  }

  const runTurn = async (hist: ChatMessage[]) => {
    setPhase('loading')
    setStreaming('')
    setQuote('')
    let acc = ''
    let q = ''
    try {
      const done = await streamChat(
        rawContent,
        hist,
        (qq) => { q = qq; setQuote(qq) },
        (delta) => { acc += delta; setStreaming(acc); scrollDown() },
      )
      // 헬스코치 턴: 인터뷰 진행 단계는 유지하고 루틴 카드만 추가
      if (done.phase === 'routine' || done.phase === 'routine_question') {
        const marked = [...hist]
        for (let i = marked.length - 1; i >= 0; i--) {
          if (marked[i].role === 'user') { marked[i] = { ...marked[i], kind: 'routine_request' }; break }
        }
        const agentMsg: ChatMessage = { role: 'assistant', content: acc, routine: done.routine }
        const next = [...marked, agentMsg]
        setMessages(next)
        setHistory(next)
        setStreaming('')
        setPhase(prevPhaseRef.current === 'loading' ? 'topic' : prevPhaseRef.current)
        scrollDown()
        return
      }
      const agentMsg: ChatMessage = { role: 'assistant', content: acc, quote: q || undefined, schedules: done.schedules ?? undefined }
      const next = [...hist, agentMsg]
      setMessages(next)
      setHistory(next)
      setStreaming('')
      const p = done.phase as Phase
      setPhase(p)
      prevPhaseRef.current = p
      setCurrentTopic(done.topic ?? undefined)
      if (done.phase === 'schedule_preview' && done.schedules) {
        setPreview(done.schedules)
        // 확정 일정은 기본 선택, 미정은 선택 해제 (와이어프레임 ③)
        setChecked(Object.fromEntries(done.schedules.map((s) => [s.id, s.status === 'confirmed'])))
      }
      scrollDown()
    } catch {
      setPhase('finished')
      setStreaming('')
      setMessages((prev) => [...prev, { role: 'assistant', content: '연결에 문제가 있었어요. 그래도 일기 완성은 가능해요!' }])
    }
  }

  useEffect(() => {
    if (!rawContent.trim()) { nav('/write'); return }
    if (startedRef.current) return
    startedRef.current = true
    if (history.length === 0) runTurn([])
    else setPhase('finished')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const send = () => {
    if (!input.trim() || phase === 'loading') return
    const next: ChatMessage[] = [...messages, { role: 'user' as const, content: input.trim() }]
    setMessages(next)
    setHistory(next)
    setInput('')
    scrollDown()
    runTurn(next)
  }

  const registerSchedules = async () => {
    const selected = preview.filter((s) => checked[s.id])
    if (selected.length > 0) await api.addSchedules(selected)
    setRegistered(true)
    const next: ChatMessage[] = [...messages, { role: 'user' as const, content: selected.length > 0 ? `일정 ${selected.length}건 등록했어` : '일정 등록은 건너뛸게' }]
    setMessages(next)
    setHistory(next)
    runTurn(next)
  }

  const topicCount = messages.filter((m) => m.quote).length

  return (
    <>
      <div className="header">
        <span><span className="back" onClick={() => nav('/write')}>←</span> 하루 깊이 들여다보기</span>
        <span className="sub">{topicCount > 0 ? `${topicCount}개 주제 감지됨` : '에이전트 대화'}</span>
      </div>
      <div className="body chat-col">
        {messages.map((m, i) => (
          <div key={i} className={`msg ${m.role === 'user' ? 'user' : 'agent'}${m.schedules || m.routine ? ' wide' : ''}`}>
            {m.quote && <div className="quote">{m.quote}</div>}
            {m.role === 'assistant' ? '🤖 ' : ''}{m.content}
            {m.routine && <RoutineCard routine={m.routine} />}
            {m.schedules && (
              <div className="card solid" style={{ marginTop: 8 }}>
                <div className="section-title">📅 내일 일정 미리보기</div>
                {m.schedules.map((s) => (
                  <div key={s.id} className="schedule-item">
                    <input
                      type="checkbox"
                      checked={checked[s.id] ?? false}
                      disabled={registered}
                      onChange={(e) => setChecked((prev) => ({ ...prev, [s.id]: e.target.checked }))}
                    />
                    <span className="grow">{s.datetime.slice(11, 16)} {s.title}</span>
                    <span className={`badge ${s.status === 'confirmed' ? 'blue' : 'gray'}`}>
                      {s.status === 'confirmed' ? '확정' : '미정'}
                    </span>
                  </div>
                ))}
                {!registered && (
                  <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                    <button className="btn small" onClick={registerSchedules}>선택한 일정 등록</button>
                    <button className="btn small gray" onClick={registerSchedules}>건너뛰기</button>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
        {streaming && (
          <div className="msg agent">
            {quote && <div className="quote">{quote}</div>}
            🤖 {streaming}
          </div>
        )}
        {phase === 'loading' && !streaming && <div className="msg agent">🤖 …</div>}
        {phase === 'finished' && (
          <button className="btn" onClick={() => nav('/complete')}>일기 완성하기</button>
        )}
        <div ref={bottomRef} />
      </div>
      {(phase === 'topic' || phase === 'tomorrow' || phase === 'loading') && (
        <div className="input-bar">
          <input ref={fileRef} type="file" accept="image/*" multiple hidden onChange={(e) => attachPhotos(e.target.files)} />
          <button className="btn gray small" aria-label="사진 첨부" onClick={() => fileRef.current?.click()}>📷</button>
          <input
            className="input-box"
            placeholder="답변을 입력하세요... (모르면 '패스')"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter' && !e.nativeEvent.isComposing) send() }}
            aria-label="답변 입력"
          />
          <button className="btn small" onClick={send} disabled={phase === 'loading'}>전송</button>
        </div>
      )}
      {toast && <div className="toast">{toast}</div>}
    </>
  )
}
