import type { ChatMessage, DetectedSpan, DiaryEntry, DiaryMetadata, FitnessRoutine, Photo, Schedule, WeeklyStats } from '../types'

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json() as Promise<T>
}

export const api = {
  diaries: () => fetch('/api/diaries').then((r) => json<DiaryEntry[]>(r)),
  diary: (date: string) => fetch(`/api/diaries/${date}`).then((r) => (r.status === 404 ? null : json<DiaryEntry>(r))),
  saveDiary: (entry: DiaryEntry) =>
    fetch('/api/diaries', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(entry) }).then((r) => json<DiaryEntry>(r)),
  schedules: (date?: string) => fetch(`/api/schedules${date ? `?date=${date}` : ''}`).then((r) => json<Schedule[]>(r)),
  addSchedules: (schedules: Schedule[]) =>
    fetch('/api/schedules', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(schedules) }).then((r) => json<Schedule[]>(r)),
  toggleSchedule: (id: string, done: boolean) =>
    fetch(`/api/schedules/${id}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ done }) }).then((r) => json<Schedule>(r)),
  weeklyStats: () => fetch('/api/stats/weekly').then((r) => json<WeeklyStats>(r)),
  deleteDiary: (date: string) => fetch(`/api/diaries/${date}`, { method: 'DELETE' }),
  deleteSchedule: (id: string) => fetch(`/api/schedules/${id}`, { method: 'DELETE' }),

  uploadPhotos: (files: FileList | File[], linkedTopic?: string) => {
    const form = new FormData()
    for (const f of Array.from(files)) form.append('files', f)
    if (linkedTopic) form.append('linkedTopic', linkedTopic)
    return fetch('/api/photos', { method: 'POST', body: form }).then((r) => json<Photo[]>(r))
  },

  detect: (text: string) =>
    fetch('/agent/detect', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text }) }).then((r) => json<{ spans: DetectedSpan[] }>(r)),
  extract: (diaryText: string, history: ChatMessage[]) =>
    fetch('/agent/extract', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ diaryText, history: history.map((m) => ({ role: m.role, content: m.content, kind: m.kind })) }),
    }).then((r) => json<DiaryMetadata>(r)),
  enrich: (rawContent: string, history: ChatMessage[]) =>
    fetch('/agent/enrich', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rawContent, history: history.map((m) => ({ role: m.role, content: m.content, kind: m.kind })) }),
    }).then((r) => json<{ enrichedContent: string; hashtags: string[] }>(r)),
  agentStatus: () =>
    fetch('/agent/status').then((r) =>
      json<{ mode: string; llm: boolean; mcp: { notion: { connected: boolean; mock: boolean }; calendar: { connected: boolean; mock: boolean } } }>(r),
    ),
  mcpNotion: (payload: { date: string; content: string }) =>
    fetch('/agent/mcp/notion', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }).then((r) =>
      json<{ ok: boolean; mock: boolean; message: string; url?: string }>(r),
    ),
  mcpCalendar: (payload: { schedules: Schedule[] }) =>
    fetch('/agent/mcp/calendar', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }).then((r) =>
      json<{ ok: boolean; mock: boolean; message: string }>(r),
    ),
}

export interface ChatDone {
  phase: 'topic' | 'tomorrow' | 'schedule_preview' | 'finished' | 'routine' | 'routine_question'
  topic?: string
  schedules?: Schedule[]
  routine?: FitnessRoutine
}

/** SSE 스트리밍 채팅 — quote/delta/done 이벤트를 콜백으로 전달 */
export async function streamChat(
  diaryText: string,
  history: ChatMessage[],
  onQuote: (quote: string) => void,
  onDelta: (delta: string) => void,
): Promise<ChatDone> {
  const res = await fetch('/agent/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ diaryText, history: history.map((m) => ({ role: m.role, content: m.content, kind: m.kind })) }),
  })
  if (!res.ok || !res.body) throw new Error('chat failed')

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  let done: ChatDone = { phase: 'finished' }

  for (;;) {
    const { value, done: eof } = await reader.read()
    if (eof) break
    buffer += decoder.decode(value, { stream: true })
    const parts = buffer.split('\n\n')
    buffer = parts.pop() ?? ''
    for (const part of parts) {
      const line = part.trim()
      if (!line.startsWith('data:')) continue
      const evt = JSON.parse(line.slice(5))
      if (evt.quote) onQuote(evt.quote)
      else if (evt.delta) onDelta(evt.delta)
      else if (evt.done) done = evt as ChatDone
    }
  }
  return done
}
