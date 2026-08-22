import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import TabBar from '../components/TabBar'
import { saveDraft, useDiaryStore } from '../store/diaryStore'
import type { DetectedSpan } from '../types'

const TYPE_LABEL: Record<string, string> = {
  workout: '운동', meeting: '회의', study: '공부', expense: '지출', activity: '활동',
}
const TYPE_COLOR: Record<string, string> = {
  workout: '#3E7A52', meeting: '#3D55B8', study: '#C9962F', expense: '#C0392B', activity: '#7852A0',
}

/** 감지 span을 하이라이트한 HTML 조각 목록 생성 */
function segments(text: string, spans: DetectedSpan[]) {
  const sorted = [...spans].sort((a, b) => a.start - b.start)
  const out: { text: string; type?: string }[] = []
  let pos = 0
  for (const s of sorted) {
    if (s.start > pos) out.push({ text: text.slice(pos, s.start) })
    out.push({ text: text.slice(s.start, s.end), type: s.type })
    pos = s.end
  }
  if (pos < text.length) out.push({ text: text.slice(pos) })
  return out
}

const WEATHERS = ['☀️', '⛅', '☁️', '🌧️', '❄️'] as const

export default function DiaryWrite() {
  const nav = useNavigate()
  const { rawContent, setRawContent, date, photos, addPhotos } = useDiaryStore()
  const [spans, setSpans] = useState<DetectedSpan[]>([])
  const [saved, setSaved] = useState(false)
  const [toast, setToast] = useState('')
  const [weather, setWeather] = useState<string>(() => localStorage.getItem(`dailymate-weather-${date}`) ?? '☀️')
  const backdropRef = useRef<HTMLDivElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const detectTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const saveTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  const attachPhotos = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    try {
      addPhotos(await api.uploadPhotos(files))
    } catch {
      setToast('사진 업로드에 실패했어요. jpg/png 10MB 이하만 올릴 수 있어요.')
      setTimeout(() => setToast(''), 3000)
    }
  }

  useEffect(() => {
    localStorage.setItem(`dailymate-weather-${date}`, weather)
  }, [weather, date])

  useEffect(() => {
    // 초기 로드 시에도 기존 초안에 대한 감지 실행
    if (rawContent) api.detect(rawContent).then((r) => setSpans(r.spans)).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const onChange = (text: string) => {
    setRawContent(text)
    setSaved(false)
    clearTimeout(detectTimer.current)
    clearTimeout(saveTimer.current)
    // N3: 1.5초 디바운스로 활동 감지
    detectTimer.current = setTimeout(() => {
      api.detect(text).then((r) => setSpans(r.spans)).catch(() => {})
    }, 1500)
    // F2-1: 2초 디바운스 localStorage 자동 저장
    saveTimer.current = setTimeout(() => {
      saveDraft(text)
      setSaved(true)
    }, 2000)
  }

  const detected = useMemo(() => [...new Set(spans.map((s) => s.type))], [spans])
  const segs = useMemo(() => segments(rawContent, spans), [rawContent, spans])

  return (
    <>
      <div className="header">
        <span><span className="back" onClick={() => nav('/')}>←</span> 오늘의 일기</span>
        <span className="sub">{date.replaceAll('-', '.')} · {saved ? '자동 저장됨 ✓' : '작성 중'}</span>
      </div>
      <div className="body">
        <div>
          <div className="diary-strip">
            <span>{date.replaceAll('-', '. ')}.</span>
            <span className="weather" role="group" aria-label="오늘의 날씨">
              {WEATHERS.map((w) => (
                <button key={w} className={weather === w ? 'on' : ''} aria-label={`날씨 ${w}`} onClick={() => setWeather(w)}>{w}</button>
              ))}
            </span>
          </div>
          <div className="editor-wrap">
            <div className="editor-backdrop" ref={backdropRef}>
              {segs.map((s, i) => (s.type ? <span key={i} className={`hl ${s.type}`}>{s.text}</span> : <span key={i}>{s.text}</span>))}
            </div>
            <textarea
              className="editor-input"
              placeholder="오늘 하루는 어땠나요? 자유롭게 써보세요."
              value={rawContent}
              onChange={(e) => onChange(e.target.value)}
              onScroll={(e) => { if (backdropRef.current) backdropRef.current.scrollTop = e.currentTarget.scrollTop }}
              rows={11}
              aria-label="일기 본문"
            />
          </div>
        </div>

        {detected.length > 0 && (
          <div className="hint">
            {detected.map((t) => (
              <span key={t}><span className="dot" style={{ background: TYPE_COLOR[t] }} /> {TYPE_LABEL[t]} 감지</span>
            ))}
          </div>
        )}

        <div className="enrich-note">
          에이전트가 일기 속 활동을 실시간으로 감지해 밑줄을 그어요.<br />
          작성을 마치면 감지된 내용을 바탕으로 질문을 드릴게요.
        </div>

        {photos.length > 0 && (
          <div className="photo-strip">
            {photos.map((p) => (
              <div key={p.id} className="photo"><img src={p.filename} alt={p.caption ?? '첨부 사진'} /></div>
            ))}
          </div>
        )}

        <input ref={fileRef} type="file" accept="image/*" multiple hidden onChange={(e) => attachPhotos(e.target.files)} />
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn gray small" style={{ flex: 1 }} onClick={() => fileRef.current?.click()}>사진 첨부</button>
          <button className="btn" style={{ flex: 2 }} disabled={rawContent.trim().length < 5} onClick={() => { saveDraft(rawContent); nav('/chat') }}>
            다 썼어요 — 대화 시작하기
          </button>
        </div>
      </div>
      {toast && <div className="toast">{toast}</div>}
      <TabBar />
    </>
  )
}
