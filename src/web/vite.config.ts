import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Aspire 서비스 디스커버리로 주입된 URL 사용 (services__{name}__http__0)
const apiTarget = process.env.services__api__http__0 ?? 'http://localhost:5590'
const agentTarget = process.env.services__agent__http__0 ?? 'http://localhost:5591'

export default defineConfig({
  plugins: [react()],
  server: {
    port: parseInt(process.env.PORT ?? '5173'),
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/photos': { target: apiTarget, changeOrigin: true },
      '/agent': { target: agentTarget, changeOrigin: true },
    },
  },
})
