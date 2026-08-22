import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Aspire AppHost injects PORT for the "web" resource
    port: parseInt(process.env.PORT ?? '5173', 10),
    host: true,
    proxy: {
      // Aspire injects the api endpoint via service discovery env vars
      '/api': {
        target:
          process.env.services__api__http__0 ??
          process.env.services__api__https__0 ??
          'http://localhost:5047',
        changeOrigin: true,
      },
    },
  },
})
