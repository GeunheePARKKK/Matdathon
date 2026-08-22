export type SpanType = 'workout' | 'meeting' | 'study' | 'expense' | 'activity'

export interface DetectedSpan {
  start: number
  end: number
  type: SpanType
}

export interface Schedule {
  id: string
  title: string
  datetime: string
  status: 'confirmed' | 'tentative'
  source: string
  done: boolean
}

export interface Photo {
  id: string
  filename: string
  caption?: string
  linkedTopic?: SpanType
}

export interface DiaryEntry {
  date: string
  rawContent: string
  enrichedContent: string
  hashtags: string[]
  photos: Photo[]
  metadata?: DiaryMetadata
  createdAt: string
}

export interface DiaryMetadata {
  workouts: { exercise: string; weight?: string; sets?: string; note?: string }[]
  meetings: { title: string; notes: string; photoIds: string[] }[]
  studies: { topic: string; detail: string }[]
  expenses: { item: string; amount: number; category: string }[]
  schedules: Schedule[]
}

export interface ChatMessage {
  role: 'user' | 'assistant'
  content: string
  quote?: string
  schedules?: Schedule[]
  kind?: 'routine_request'
  routine?: FitnessRoutine
}

export interface RoutineExercise {
  name: string
  sets: number
  reps: number
  rpe: number
  rest_sec: number
  is_warmup?: boolean
}

export interface FitnessRoutine {
  type: 'fitness_routine'
  date: string
  target: string
  condition_summary: string
  exercises: RoutineExercise[]
  notes?: string
  estimated_minutes?: number
}

export interface WeeklyStats {
  diaryDays: number
  meetings: number
  schedules: number
}
