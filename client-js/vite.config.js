import { defineConfig } from 'vite';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  server: {
    port: 5174
  },
  build: {
    lib: {
      entry: path.resolve(__dirname, 'serviceBusApi.ts'),
      name: 'ServiceBusAPI',
      formats: ['iife'],
      fileName: () => 'servicebus-api.js'
    },
    rollupOptions: {
      output: {
        // Put everything in a single file
        inlineDynamicImports: true,
        // Expose as window.ServiceBusAPI
        extend: true,
        globals: {
          'ServiceBusAPI': 'ServiceBusAPI'
        }
      }
    },
    outDir: path.resolve(__dirname, '../src/wwwroot/js'),
    emptyOutDir: false,
    minify: 'terser'
  },
  resolve: {
    alias: {
      // Use rhea's pre-built browser bundle instead of Node.js source
      'rhea': path.resolve(__dirname, 'node_modules/rhea/dist/rhea-umd.js')
    }
  },
  define: {
    'global': 'globalThis',
    'process.env.NODE_DEBUG': 'false'
  },
  optimizeDeps: {
    include: ['buffer', 'events', 'rhea'],
    exclude: []
  }
});
