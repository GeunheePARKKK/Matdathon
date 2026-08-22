import { useEffect, useState } from 'react'
import { api } from '../api/client'
import TabBar from '../components/TabBar'
import { todayStr } from '../store/diaryStore'

interface Status {
  mode: string
  llm: boolean
  mcp: {
    notion: { connected: boolean; mock: boolean }
    calendar: { connected: boolean; mock: boolean }
  }
}

export default function Integrations() {
  const [status, setStatus] = useState<Status | null>(null)
  const [toast, setToast] = useState('')

  useEffect(() => {
    api.agentStatus().then(setStatus).catch(() => {})
  }, [])

  const showToast = (msg: string) => {
    setToast(msg)
    setTimeout(() => setToast(''), 3000)
  }

  const exportDiary = async (format: 'markdown' | 'json') => {
    const res = await fetch('/api/export', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ date: todayStr(), format }),
    })
    if (!res.ok) { showToast('⚠️ 오늘 저장된 일기가 없어요'); return }
    const blob = await res.blob()
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `diary-${todayStr()}.${format === 'json' ? 'json' : 'md'}`
    a.click()
    URL.revokeObjectURL(a.href)
  }

  const rows = [
    { icon: '📓', name: 'Notion', desc: '일기 본문 + 메타데이터 저장', s: status?.mcp.notion },
    { icon: '📅', name: 'Google Calendar', desc: '확정 일정 동기화', s: status?.mcp.calendar },
  ]

  return (
    <>
      <div className="header">🔗 연동<span className="sub">MCP · 내보내기</span></div>
      <div className="body">
        <div>
          <div className="section-title">🤖 에이전트</div>
          <div className="card solid">
            <div className="extract-item">
              모드: {status ? (status.llm ? `LLM (${status.mode})` : '목(mock) 모드') : '확인 중…'}
            </div>
            <div style={{ fontSize: 11.5, color: '#8a93a0' }}>
              Microsoft Agent Framework + GitHub Copilot SDK · LLM 미연결 시 규칙 기반 목 모드로 동작해요.
            </div>
          </div>
        </div>

        <div>
          <div className="section-title">🔌 MCP 연동</div>
          <div className="card solid">
            {rows.map((r) => (
              <div key={r.name} className="schedule-item">
                <span>{r.icon}</span>
                <span className="grow">
                  {r.name}
                  <div style={{ fontSize: 11, color: '#8a93a0' }}>{r.desc}</div>
                </span>
                <span className={`badge ${r.s?.connected ? '' : 'gray'}`}>
                  {r.s ? (r.s.connected ? '연결됨' : '목 모드') : '…'}
                </span>
              </div>
            ))}
            <div style={{ fontSize: 11.5, color: '#8a93a0', marginTop: 8 }}>
              환경 변수(NOTION_MCP_TOKEN 등)를 설정하면 실제 연동으로 전환돼요.
            </div>
          </div>
        </div>

        <div>
          <div className="section-title">📤 내보내기 (오늘 일기)</div>
          <div style={{ display: 'flex', gap: 8 }}>
            <button className="btn ghost small" style={{ flex: 1 }} onClick={() => exportDiary('markdown')}>Markdown</button>
            <button className="btn ghost small" style={{ flex: 1 }} onClick={() => exportDiary('json')}>JSON</button>
          </div>
        </div>
      </div>
      {toast && <div className="toast" role="status" aria-live="polite">{toast}</div>}
      <TabBar />
    </>
  )
}
