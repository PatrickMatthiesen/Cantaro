import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

function readEnvValue(name: string): string | undefined {
  const exactMatch = process.env[name]
  if (exactMatch) {
    return exactMatch
  }

  const caseInsensitiveKey = Object.keys(process.env).find(
    (key) => key.toLowerCase() === name.toLowerCase(),
  )

  return caseInsensitiveKey ? process.env[caseInsensitiveKey] : undefined
}

const configuredApiBaseUrl =
  readEnvValue('services__api__https__0') ||
  readEnvValue('services__api__http__0') ||
  readEnvValue('API_HTTPS') ||
  readEnvValue('API_HTTP') ||
  ''

const apiProxyTarget = configuredApiBaseUrl || 'https://localhost:7203'

// https://vite.dev/config/
export default defineConfig(({ command }) => ({
  define: {
    __CANTARO_TRUSTED_API_BASE_URL__: JSON.stringify(command === 'serve' ? apiProxyTarget : configuredApiBaseUrl),
  },
  plugins: [react(), tailwindcss()],
  server: {
    strictPort: true,
    proxy: {
      '/api': {
        target: apiProxyTarget,
        changeOrigin: true,
        secure: false
      },
    },
  },
}))
