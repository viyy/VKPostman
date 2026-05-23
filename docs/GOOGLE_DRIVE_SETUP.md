# Google Drive backup — getting an OAuth Client ID

VK Postman's Drive backup runs entirely in your browser (no server). To talk to
Google Drive it needs a **Google OAuth Client ID** that *you* create. The Client
ID is **public** (there is no client secret in this flow), so it is safe to paste
into the app or commit to the repo.

This is a one-time setup, ~5 minutes.

## 1. Create / pick a project
1. Go to <https://console.cloud.google.com/>.
2. Top bar → project dropdown → **New Project** (e.g. "VK Postman"), then select it.

## 2. Enable the Drive API
1. **APIs & Services → Library** (or search "Google Drive API").
2. Open **Google Drive API** → **Enable**.

## 3. Configure the OAuth consent screen
1. **APIs & Services → OAuth consent screen** (newer UI: **Google Auth Platform → Branding / Audience**).
2. User type: **External** → Create.
3. Fill the required fields: App name, *User support email*, *Developer contact*. Save and continue (optional fields can be skipped).
4. **Audience / Test users** → add **your own Google account** as a test user. In "Testing" mode this lets you use the app without Google verifying it.
   - You don't need to add scopes manually; the app requests `drive.appdata` at sign-in time.

## 4. Create the OAuth Client ID
1. **APIs & Services → Credentials → Create credentials → OAuth client ID**.
2. **Application type: Web application**.
3. Name: anything (e.g. "VK Postman web").
4. **Authorized JavaScript origins** → add one entry per origin you open the app from. An *origin* is `scheme + host + port`, with **no path**:
   - `https://viyy.github.io` — the GitHub Pages site
   - `http://localhost:5173` — `npm run dev`
   - `http://localhost:4173` — `npm run preview` (optional)
5. **Leave "Authorized redirect URIs" empty** — the token flow doesn't use them.
6. **Create**.

## 5. Use the Client ID
The dialog shows your **Client ID** ending in `…apps.googleusercontent.com`. Use it in one of three ways (any one is enough; localStorage wins over the others):

### a) Paste it in the app (per device)
Top bar → **Data tools (🗂️) → Google Drive backup** → paste the Client ID → **Save → Connect**. Stored in this browser's `localStorage`.

### b) Bake it in at build time (recommended for a shared deploy)
Set an env var so every visitor gets it without pasting:

```bash
# .env (or .env.local) in VkPostmanWeb/
VITE_GOOGLE_CLIENT_ID=xxxxxxxx.apps.googleusercontent.com
```

For the GitHub Pages deploy, set it in the workflow before `npm run build`
(e.g. a repo **Variable** `VITE_GOOGLE_CLIENT_ID` exposed as `env:` on the build step).

### c) Hard-code it in source
Edit `VkPostmanWeb/src/lib/gdrive.svelte.ts` and set:

```ts
const HARDCODED_CLIENT_ID = 'xxxxxxxx.apps.googleusercontent.com';
```

## Notes
- **Origins are exact.** `https://viyy.github.io/VKPostman/` is **not** a valid origin — use just `https://viyy.github.io`. Use whatever the browser address bar shows, minus the path.
- **"Google hasn't verified this app"** is expected in Testing mode. Click **Advanced → Go to … (unsafe)**. It's safe here: the app requests only the hidden **app-data folder** (`drive.appdata`), which cannot see the rest of your Drive.
- **No client secret** is used by this browser-only flow, so the Client ID is not sensitive.
- Access tokens are short-lived (~1h) and kept in memory only; you re-authorize per session (usually a silent popup if you're already signed into Google).
- If you change the site's domain later, add the new origin in step 4.
