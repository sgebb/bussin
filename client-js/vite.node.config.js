import { defineConfig } from 'vite';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Node/ESM build of the Service Bus client, consumed by the bussin-mcp server.
// Unlike vite.config.js this does NOT alias rhea to its browser UMD bundle and does
// not bundle dependencies -- Node resolves them normally at runtime.
export default defineConfig({
  build: {
    lib: {
      entry: path.resolve(__dirname, 'serviceBusApi.ts'),
      formats: ['es'],
      fileName: () => 'index.js'
    },
    rollupOptions: {
      external: ['rhea', 'buffer', 'events'],
      output: { inlineDynamicImports: true }
    },
    outDir: path.resolve(__dirname, 'dist/node'),
    emptyOutDir: true,
    minify: false,
    target: 'node22',
    ssr: true
  }
});
