import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import TabBar from '../components/TabBar'
import type { DiaryEntry } from '../types'
import { renderBold } from './DiaryComplete'

const DAYS = ['일', '월', '화', '수', '목', '금', '토']

export default function DiaryDetail() {
  const nav = useNavigate()
  const { date } = useParams<{ date: string }>()
  const [entry, setEntry] = useState<DiaryEntry | null>(null)
  const [notFound, setNotFound] = useState(false)

  useEffect(() => {
    if (!date) return
    api.diary(date).then((e) => (e ? setEntry(e) : setNotFound(true))).catch(() => setNotFound(true))
  }, [date])

  const d = date ? new Date(date) : new Date()
  const title = `${d.getMonth() + 1}월 ${d.getDate()}일 ${DAYS[d.getDay()]}요일`

  return (
    <>
      <div className="header">
        <span><span className="back" onClick={() => nav(-1)}>←</span> 일기 보기</span>
        <span className="sub">{date?.replaceAll('-', '.')}</span>
      </div>
      <div className="body">
        {notFound && <div className="empty">일기를 찾을 수 없어요</div>}
        {entry && (
          <div className="diary-page">
            <div className="diary-title">{title} 일기</div>
            <div className="diary-text">{renderBold(entry.enrichedContent || entry.rawContent)}</div>
            {entry.photos.length > 0 && (
              <div className="photo-strip">
                {entry.photos.map((p) => (
                  <div key={p.id} className="photo"><img src={p.filename} alt={p.caption ?? '첨부 사진'} /></div>
                ))}
              </div>
            )}
            {entry.hashtags.length > 0 && <div className="tags">{entry.hashtags.join(' ')}</div>}
          </div>
        )}
        {entry?.enrichedContent && (
          <div className="ai-note">
            <span className="pen-mark">파란 밑줄</span>은 AI가 대화 내용을 바탕으로 덧쓴 문장이에요.
          </div>
        )}
      </div>
      <TabBar />
    </>
  )
}
