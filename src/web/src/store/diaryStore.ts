import { create } from 'zustand'
import type { ChatMessage, Photo } from '../types'

export function todayStr(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

interface DiaryState {
  date: string
  rawContent: string
  history: ChatMessage[]
  photos: Photo[]
  enrichedContent: string
  hashtags: string[]
  setRawContent: (text: string) => void
  setHistory: (h: ChatMessage[]) => void
  addPhotos: (p: Photo[]) => void
  setEnriched: (content: string, hashtags: string[]) => void
  reset: () => void
}

const draftKey = () => `dailymate-draft-${todayStr()}`
const photosKey = () => `dailymate-photos-${todayStr()}`

export const useDiaryStore = create<DiaryState>((set) => ({
  date: todayStr(),
  rawContent: localStorage.getItem(draftKey()) ?? '',
  history: [],
  photos: JSON.parse(localStorage.getItem(photosKey()) ?? '[]') as Photo[],
  enrichedContent: '',
  hashtags: [],
  setRawContent: (rawContent) => {
    set({ rawContent })
  },
  setHistory: (history) => set({ history }),
  addPhotos: (added) =>
    set((s) => {
      const photos = [...s.photos, ...added]
      localStorage.setItem(photosKey(), JSON.stringify(photos))
      return { photos }
    }),
  setEnriched: (enrichedContent, hashtags) => set({ enrichedContent, hashtags }),
  reset: () => set({ rawContent: '', history: [], photos: [], enrichedContent: '', hashtags: [] }),
}))

export function saveDraft(text: string) {
  localStorage.setItem(draftKey(), text)
}
