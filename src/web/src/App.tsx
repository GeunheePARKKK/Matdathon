import { Route, Routes } from 'react-router-dom'
import Home from './pages/Home'
import DiaryWrite from './pages/DiaryWrite'
import AgentChat from './pages/AgentChat'
import DiaryComplete from './pages/DiaryComplete'
import Records from './pages/Records'
import DiaryDetail from './pages/DiaryDetail'
import Integrations from './pages/Integrations'

export default function App() {
  return (
    <div className="phone">
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/write" element={<DiaryWrite />} />
        <Route path="/chat" element={<AgentChat />} />
        <Route path="/complete" element={<DiaryComplete />} />
        <Route path="/records" element={<Records />} />
        <Route path="/diary/:date" element={<DiaryDetail />} />
        <Route path="/integrations" element={<Integrations />} />
      </Routes>
    </div>
  )
}
