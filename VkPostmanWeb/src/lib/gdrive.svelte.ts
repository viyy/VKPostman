// Client-side Google Drive backup/sync. No server: uses Google Identity
// Services (GIS) token flow + the Drive REST API, storing a single backup file
// in the hidden appDataFolder (scope drive.appdata — least privilege).
//
// The OAuth Client ID is public and supplied by the user (stored locally), so
// nothing secret ships in the build. Access tokens are short-lived and kept in
// memory only; a pure client-side app cannot hold refresh tokens.

/* eslint-disable @typescript-eslint/no-explicit-any */

const SCOPE = 'https://www.googleapis.com/auth/drive.appdata';
const FILE_NAME = 'vk-postman-backup.json';
const GIS_SRC = 'https://accounts.google.com/gsi/client';

const LS_CLIENT_ID = 'vkp.gdrive.clientId';
const LS_LAST = 'vkp.gdrive.lastBackupAt';

// Optional default Client ID baked into the build. Provide it via either a
// build-time env var (VITE_GOOGLE_CLIENT_ID) or by hard-coding the constant
// below — the Client ID is public, so committing it is safe. A value saved in
// localStorage (pasted in the UI) always takes precedence over these.
const HARDCODED_CLIENT_ID = '';
const DEFAULT_CLIENT_ID =
  (import.meta.env.VITE_GOOGLE_CLIENT_ID ?? '').trim() || HARDCODED_CLIENT_ID;

class GDrive {
  clientId = $state(localStorage.getItem(LS_CLIENT_ID) ?? DEFAULT_CLIENT_ID);
  connected = $state(false);
  lastBackupAt = $state<number>(Number(localStorage.getItem(LS_LAST)) || 0);
  busy = $state(false);

  #token: string | null = null;
  #tokenExpiry = 0;
  #tokenClient: any = null;
  #gisReady = false;

  get configured(): boolean {
    return this.clientId.trim().length > 0;
  }

  setClientId(id: string): void {
    this.clientId = id.trim();
    localStorage.setItem(LS_CLIENT_ID, this.clientId);
    // Reset auth — the old token/client belongs to the previous client id.
    this.#tokenClient = null;
    this.#token = null;
    this.connected = false;
  }

  #markBackedUp(): void {
    this.lastBackupAt = Date.now();
    localStorage.setItem(LS_LAST, String(this.lastBackupAt));
  }

  async #loadGis(): Promise<void> {
    if (this.#gisReady && (window as any).google?.accounts?.oauth2) return;
    await new Promise<void>((resolve, reject) => {
      const ready = () => (window as any).google?.accounts?.oauth2;
      if (ready()) return resolve();
      const existing = document.querySelector(`script[src="${GIS_SRC}"]`);
      if (existing) {
        const poll = () => (ready() ? resolve() : setTimeout(poll, 50));
        return poll();
      }
      const s = document.createElement('script');
      s.src = GIS_SRC;
      s.async = true;
      s.defer = true;
      s.onload = () => resolve();
      s.onerror = () => reject(new Error('Could not load Google sign-in (are you offline?).'));
      document.head.appendChild(s);
    });
    this.#gisReady = true;
  }

  /** Get a valid access token, prompting the user only when necessary. */
  async #ensureToken(interactive: boolean): Promise<string> {
    if (this.#token && Date.now() < this.#tokenExpiry - 60_000) return this.#token;
    if (!this.configured) throw new Error('Set your Google OAuth Client ID first.');
    await this.#loadGis();
    const oauth2 = (window as any).google.accounts.oauth2;

    return new Promise<string>((resolve, reject) => {
      if (!this.#tokenClient) {
        this.#tokenClient = oauth2.initTokenClient({
          client_id: this.clientId,
          scope: SCOPE,
          callback: (resp: any) => {
            if (resp?.error) {
              this.connected = false;
              reject(new Error(resp.error_description || resp.error));
              return;
            }
            this.#token = resp.access_token;
            this.#tokenExpiry = Date.now() + (Number(resp.expires_in) || 3600) * 1000;
            this.connected = true;
            resolve(this.#token!);
          },
          error_callback: (err: any) => {
            this.connected = false;
            reject(new Error(err?.message || 'Authorization was cancelled.'));
          },
        });
      }
      try {
        this.#tokenClient.requestAccessToken({ prompt: interactive ? 'consent' : '' });
      } catch (e) {
        reject(e as Error);
      }
    });
  }

  async connect(): Promise<void> {
    this.busy = true;
    try {
      await this.#ensureToken(true);
    } finally {
      this.busy = false;
    }
  }

  signOut(): void {
    const t = this.#token;
    const oauth2 = (window as any).google?.accounts?.oauth2;
    if (t && oauth2) oauth2.revoke(t, () => {});
    this.#token = null;
    this.#tokenExpiry = 0;
    this.connected = false;
  }

  async #api(url: string, init: RequestInit): Promise<Response> {
    let token: string;
    try {
      token = await this.#ensureToken(false);
    } catch {
      token = await this.#ensureToken(true);
    }
    const withAuth = (tk: string): RequestInit => ({
      ...init,
      headers: { ...(init.headers as Record<string, string>), Authorization: `Bearer ${tk}` },
    });
    let res = await fetch(url, withAuth(token));
    if (res.status === 401) {
      this.#token = null;
      const fresh = await this.#ensureToken(true);
      res = await fetch(url, withAuth(fresh));
    }
    return res;
  }

  async #findFile(): Promise<{ id: string; modifiedTime: string } | null> {
    const q = encodeURIComponent(`name='${FILE_NAME}'`);
    const url =
      `https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q=${q}&fields=files(id,name,modifiedTime)`;
    const res = await this.#api(url, { method: 'GET' });
    if (!res.ok) throw new Error(`Drive list failed (${res.status}).`);
    const data = await res.json();
    const f = data.files?.[0];
    return f ? { id: f.id, modifiedTime: f.modifiedTime } : null;
  }

  /** Upload `json` as the single backup file (creating or overwriting it). */
  async backup(json: string): Promise<void> {
    this.busy = true;
    try {
      const existing = await this.#findFile();
      if (existing) {
        const res = await this.#api(
          `https://www.googleapis.com/upload/drive/v3/files/${existing.id}?uploadType=media`,
          { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: json },
        );
        if (!res.ok) throw new Error(`Upload failed (${res.status}).`);
      } else {
        const metadata = { name: FILE_NAME, parents: ['appDataFolder'] };
        const boundary = 'vkp' + Math.random().toString(16).slice(2);
        const body =
          `--${boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n` +
          `${JSON.stringify(metadata)}\r\n` +
          `--${boundary}\r\nContent-Type: application/json\r\n\r\n${json}\r\n--${boundary}--`;
        const res = await this.#api(
          'https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id',
          { method: 'POST', headers: { 'Content-Type': `multipart/related; boundary=${boundary}` }, body },
        );
        if (!res.ok) throw new Error(`Create failed (${res.status}).`);
      }
      this.#markBackedUp();
    } finally {
      this.busy = false;
    }
  }

  /** Download the backup file's parsed JSON, or null if none exists. */
  async restore(): Promise<unknown | null> {
    this.busy = true;
    try {
      const f = await this.#findFile();
      if (!f) return null;
      const res = await this.#api(
        `https://www.googleapis.com/drive/v3/files/${f.id}?alt=media`,
        { method: 'GET' },
      );
      if (!res.ok) throw new Error(`Download failed (${res.status}).`);
      return await res.json();
    } finally {
      this.busy = false;
    }
  }
}

export const gdrive = new GDrive();
