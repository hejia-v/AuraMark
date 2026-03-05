import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 700,
    rollupOptions: {
      output: {
        manualChunks: {
          milkdown: [
            '@milkdown/core',
            '@milkdown/preset-commonmark',
            '@milkdown/preset-gfm',
            '@milkdown/plugin-prism',
            '@milkdown/plugin-listener',
            '@milkdown/theme-nord',
          ],
          prism: ['prismjs'],
        },
      },
    },
  },
});
