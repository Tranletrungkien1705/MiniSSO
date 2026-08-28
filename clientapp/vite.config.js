import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// SPA build ra thẳng ../wwwroot để ASP.NET serve tĩnh. emptyOutDir=false để giữ file khác trong wwwroot.
export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: false,
    assetsDir: 'assets'
  },
  server: {
    proxy: { '/api': 'http://localhost:5080' }
  }
})
