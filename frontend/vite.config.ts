import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    restoreMocks: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5093',
    },
  },
})
