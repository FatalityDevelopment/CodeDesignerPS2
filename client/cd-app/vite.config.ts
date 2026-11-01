import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The Bridge serves the built page and /api/bridge on port 8771; the dev server forwards /api there. changeOrigin
// makes the Host header match the Bridge's listener; the browser's Origin header passes through unchanged.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@bridge-client': fileURLToPath(new URL('../vscode/src/bridgeClient.ts', import.meta.url)),
    },
  },
  server: {
    port: 5174,
    strictPort: true,
    proxy: { '/api': { target: 'http://localhost:8771', ws: true, changeOrigin: true } },
    fs: { allow: ['..'] },
  },
});
