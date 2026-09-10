/**
 * Bundle the server into a single self-contained dist/index.js.
 *
 * bussin-client-js lives in this repo and is depended on via `file:../client-js`, which npm
 * cannot resolve for anyone installing from the registry. Inlining it at build time keeps the
 * published package self-contained; everything with a real npm identity stays external so it
 * is deduped and patched normally.
 */

import { build } from 'esbuild';
import { rm } from 'node:fs/promises';

await rm('dist', { recursive: true, force: true });

const result = await build({
    entryPoints: ['src/index.ts'],
    outfile: 'dist/index.js',
    bundle: true,
    platform: 'node',
    target: 'node22',
    format: 'esm',
    sourcemap: true,
    // Inlined: bussin-client-js (not published separately).
    // External: everything declared in package.json dependencies.
    external: ['@azure/identity', '@modelcontextprotocol/sdk', 'zod', 'rhea'],
    banner: { js: '#!/usr/bin/env node' },
    logLevel: 'info',
    metafile: true
});

const bytes = Object.values(result.metafile.outputs)
    .reduce((sum, o) => sum + o.bytes, 0);
console.log(`bundled dist/index.js (${(bytes / 1024).toFixed(1)} kB total)`);
