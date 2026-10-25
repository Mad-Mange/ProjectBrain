import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
export default defineConfig({
  plugins: [react()],
  server: { port:4318, strictPort:true, proxy:{'/api':process.env.PROJECTBRAIN_DEV_API_URL ?? 'http://127.0.0.1:4317','/health':process.env.PROJECTBRAIN_DEV_API_URL ?? 'http://127.0.0.1:4317'} },
  test: {environment:'jsdom',setupFiles:['./src/test-setup.ts'],include:['src/**/*.test.tsx']},
});

