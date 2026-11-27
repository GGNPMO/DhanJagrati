import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on http://localhost:5072 during development; /api is proxied so the
// browser stays same-origin (cookie auth with SameSite=Strict).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5072',
    },
  },
})
