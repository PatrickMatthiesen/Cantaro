import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    strictPort: true,
    proxy: {
      '/api': {
        target: process.env.services__api__https__0 || process.env.services__api__http__0 || process.env.API_HTTPS || process.env.API_HTTP || 'https://localhost:7203',
        changeOrigin: true,
        secure: false
      },
    },
  },
})
