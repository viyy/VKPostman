import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    svelte(),
    VitePWA({
      registerType: 'autoUpdate',
      // An offline-first shell: cache everything the build produces so the app
      // still works with no network. IndexedDB (Dexie) handles the data side.
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,webmanifest}'],
      },
      includeAssets: ['favicon.svg'],
      manifest: {
        name: 'VK Postman',
        short_name: 'VK Postman',
        description:
          'Compose VK post suggestions with reusable templates and per-group tags.',
        theme_color: '#5181B8',
        background_color: '#EDEEF0',
        display: 'standalone',
        orientation: 'any',
        start_url: '.',
        // SVG works in Chrome 100+ and Safari 17+. If you want raster icons for
        // broader "Install" prompt support (older Android Chrome / Samsung),
        // drop icon-192.png + icon-512.png into public/ and add PNG entries here.
        icons: [
          { src: 'favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          { src: 'favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'maskable' },
        ],
      },
      devOptions: {
        enabled: true,
      },
    }),
  ],
  // Base path. GitHub Pages at https://<user>.github.io/<repo>/ needs '/<repo>/'
  // so manifest + service-worker paths resolve correctly — the deploy workflow
  // injects VITE_BASE=/<repo>/. Falls back to './' for local dev + manual deploys.
  base: process.env.VITE_BASE ?? './',
});
