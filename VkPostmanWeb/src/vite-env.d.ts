/// <reference types="svelte" />
/// <reference types="vite/client" />
/// <reference types="vite-plugin-pwa/client" />

/** App version, injected from package.json at build time (see vite.config.ts). */
declare const __APP_VERSION__: string;

// Merge our custom env var into Vite's ImportMetaEnv typing.
interface ImportMetaEnv {
  /** Optional default Google OAuth Client ID for Drive backup (public). */
  readonly VITE_GOOGLE_CLIENT_ID?: string;
}
