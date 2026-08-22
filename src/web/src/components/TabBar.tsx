import { NavLink } from 'react-router-dom'

const tabs = [
  { to: '/', icon: '🏠', label: '홈' },
  { to: '/write', icon: '✍️', label: '일기' },
  { to: '/records', icon: '📊', label: '기록' },
  { to: '/integrations', icon: '🔗', label: '연동' },
]

export default function TabBar() {
  return (
    <div className="tabbar">
      {tabs.map((t) => (
        <NavLink key={t.to} to={t.to} className={({ isActive }) => `tab${isActive ? ' active' : ''}`} end={t.to === '/'}>
          <span className="icon">{t.icon}</span>
          {t.label}
        </NavLink>
      ))}
    </div>
  )
}
