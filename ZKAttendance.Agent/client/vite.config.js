import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5201,
    // The agent's own backend. Everything the UI needs goes through it, so the
    // agent secret stays server-side and never reaches the browser.
    proxy: { '/api': { target: 'http://localhost:5200', changeOrigin: true } },
  },
  // Built output is served by the agent itself, so one process serves both.
  build: { outDir: '../wwwroot', emptyOutDir: true },
})
