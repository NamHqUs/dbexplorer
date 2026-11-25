import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The API runs on the port in Properties/launchSettings.json. Calls to /api are proxied so the browser sees one origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5105',
    },
  },
});
