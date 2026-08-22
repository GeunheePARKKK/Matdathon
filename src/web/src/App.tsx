import { useRef, useState } from 'react'
import './App.css'
import RoutineCard, { isFitnessRoutine } from './components/RoutineCard'

interface ChatMessage {
  role: 'user' | 'assistant'
  content: string
  data?: unknown
}

function App() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  // 무로그인 로컬 세션 — 대화 이력/컨디션 유지 단위
  const [sessionId] = useState(() => crypto.randomUUID())
  const listRef = useRef<HTMLDivElement>(null)

  const send = async () => {
    const text = input.trim()
    if (!text || sending) return
    setInput('')
    setSending(true)
    setMessages((prev) => [...prev, { role: 'user', content: text }])
    try {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: text, sessionId }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const reply: ChatMessage = await res.json()
      setMessages((prev) => [...prev, reply])
    } catch (err) {
      setMessages((prev) => [
        ...prev,
        { role: 'assistant', content: `오류가 발생했어요: ${String(err)}` },
      ])
    } finally {
      setSending(false)
      queueMicrotask(() =>
        listRef.current?.scrollTo({ top: listRef.current.scrollHeight }),
      )
    }
  }

  return (
    <div className="chat">
      <h1>DailyMate</h1>
      <div className="chat-list" ref={listRef}>
        {messages.length === 0 && (
          <p className="chat-empty">메시지를 보내서 왕복을 확인해보세요.</p>
        )}
        {messages.map((m, i) =>
          isFitnessRoutine(m.data) ? (
            <RoutineCard key={i} routine={m.data} />
          ) : (
            <div key={i} className={`chat-msg chat-msg--${m.role}`}>
              <span className="chat-role">{m.role === 'user' ? '나' : '에이전트'}</span>
              <p>{m.content}</p>
            </div>
          ),
        )}
      </div>
      <form
        className="chat-input"
        onSubmit={(e) => {
          e.preventDefault()
          send()
        }}
      >
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="메시지를 입력하세요..."
          disabled={sending}
        />
        <button type="submit" disabled={sending || !input.trim()}>
          {sending ? '...' : '보내기'}
        </button>
      </form>
    </div>
  )
}

export default App
