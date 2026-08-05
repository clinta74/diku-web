// defineConfig comes from vitest/config, not vite, so the `test` block below type-checks.
// This is the single-config benefit in practice: plugins and aliases are declared once
// and both the dev server and the test runner read them.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

const API_TARGET = process.env.DIKUWEB_API ?? 'http://localhost:5180'

export default defineConfig({
  plugins: [react()],

  server: {
    port: 5173,
    strictPort: true,

    // PLAN.md §3.2: auth is an HttpOnly, SameSite=Lax cookie, because the browser's native
    // EventSource cannot set an Authorization header. Proxying through the dev server keeps
    // the browser on a single origin (localhost:5173), so the cookie behaves in development
    // exactly as it will in production. Talking to Kestrel cross-origin instead would force
    // SameSite=None in dev only - which is precisely how cookie bugs ship.
    proxy: {
      '/api': {
        target: API_TARGET,
        changeOrigin: false,
        // Phase 1 note: /api/game/stream is Server-Sent Events. Buffering here would stall
        // the stream the same way a misconfigured reverse proxy does in production, so
        // verify events arrive incrementally through this proxy, not just against Kestrel.
      },
      '/health': {
        target: API_TARGET,
        changeOrigin: false,
      },
    },
  },

  // Vitest reads this same config, so path aliases and plugins are defined once.
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
})
