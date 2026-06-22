import { defineConfig } from 'vite';
import { resolve } from 'node:path';

export default defineConfig({
  root: __dirname,
  build: {
    outDir: 'dist/renderer',
    emptyOutDir: false,
    rollupOptions: { input: resolve(__dirname, 'index.html') }
  }
});
