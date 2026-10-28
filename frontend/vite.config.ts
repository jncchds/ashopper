/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Dev server proxies /api and /auth to the API, mirroring the nginx routing in production,
// so the app is same-origin in both and the refresh cookie just works.
const apiTarget = process.env.API_URL ?? 'http://localhost:5080';
const { version } = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as { version: string };

export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: {
    __APP_VERSION__: JSON.stringify(version),
  },
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
