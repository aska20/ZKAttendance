import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The API base URL is read from VITE_API_URL at build time (see .env).
// In dev we also proxy /api to the backend so the browser makes same-origin
// requests and CORS never enters the picture while developing.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET || 'http://localhost:5107',
        changeOrigin: true,
      },
    },
  },
})
