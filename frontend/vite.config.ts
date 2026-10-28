/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Dev server proxies /api and /auth to the API, mirroring the nginx routing in production,
// so the app is same-origin in both and the refresh cookie just works.
const apiTarget = process.env.API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/auth': apiTarget,
    },
  },
  build: {
    // Content-hashed file names under /assets → nginx serves them as immutable.
    assetsDir: 'assets',
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',
  },
});
