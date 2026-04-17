# VK Postman — web / PWA

Installable, offline, local-only companion to the desktop build.

- **Stack:** Svelte 5 + Vite 6 + TypeScript + Dexie (IndexedDB) + vite-plugin-pwa
- **Data:** all client-side in IndexedDB — nothing leaves the device, no backend, no account
- **Install:** works as a web app at any URL; on Android/iOS use *Add to Home Screen*; on Windows/macOS use the browser's "Install" button

## Dev

```bash
cd VkPostmanWeb
npm install
npm run dev
```

Opens at `http://localhost:5173`. Code/HMR as you edit.

## Build

```bash
npm run build
```

Produces a static site in `dist/`. Works out of a flat file host, GitHub Pages, Netlify, Cloudflare Pages, etc. The `base: './'` in `vite.config.ts` keeps relative paths, so you can drop it in any subfolder.

```bash
npm run preview   # sanity-check the built bundle locally
```

## Deploying to your phone

1. Build and push `dist/` somewhere with HTTPS (GitHub Pages is free — set the repo to "Pages: /root" and point it at `VkPostmanWeb/dist/`).
2. Open the URL in Chrome on Android (or Safari on iOS).
3. Browser menu → **Add to Home Screen** / **Install app**.
4. The PWA shows up as a standalone icon; launches without the browser chrome, works offline.

Because everything lives in IndexedDB, each device has its own copy of the data. No sync. If you want cross-device parity, export/import via JSON is the smallest thing that could work — we haven't built it yet.

## Icons

The manifest references `icon-192.png` and `icon-512.png` in `public/`. Drop any 192×192 and 512×512 PNGs there with those exact names before building, or the install prompt will fall back to the favicon. `favicon.svg` is generic VK-blue envelope; replace if you feel like it.

## Project layout

```
src/
├── main.ts              bootstrap
├── App.svelte           tab shell (Drafts / Templates / Groups)
├── app.css              global styles, matches the WPF palette
├── lib/
│   ├── types.ts         PlaceholderType enum + data-shape interfaces
│   ├── db.ts            Dexie schema + CRUD helpers
│   └── render.ts        template engine, VK link normalizer, readiness check
└── views/
    ├── DraftsView.svelte
    ├── TemplatesView.svelte
    └── GroupsView.svelte
```

## Parity with the desktop build

| Concept                | Desktop (WPF / .NET)        | Web (this project) |
|------------------------|------------------------------|---------------------|
| Domain models          | `VkPostman.Core/Models/*`   | `src/lib/types.ts` |
| Template engine        | Scriban                      | `{{ name }}` substitution (no loops) |
| Storage                | EF Core + SQLite             | Dexie + IndexedDB |
| Placeholder types      | `Text / VkLink / Url / TagList / WikiLink` | same enum values |
| WikiLink packing       | `"target\u001Fdisplay"`      | same (parity-compatible) |
| Clipboard + open URL   | `Clipboard.SetText` / `Process.Start` | `navigator.clipboard` / `window.open` |

If you ever want to sync desktop ↔ web, write an `export all rows as JSON` command on both sides (trivial — about 20 lines each) and copy the file via whatever sync you already have (OneDrive, iCloud, Syncthing, etc.).

## What this *doesn't* do (by design)

- No VK API calls
- No photo upload / preview / carousel
- No publishing queue
- No auth flow

The app's role is: fill a draft → copy the rendered text → paste into VK's post editor by hand, same as the desktop build.
