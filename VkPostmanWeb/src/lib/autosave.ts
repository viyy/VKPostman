/**
 * Debounced autosave helper — given a getter that returns the current editable
 * payload, a save function, and a debounce delay, it watches the payload and
 * saves after the user has been quiet for `delayMs`.
 *
 * Usage inside a Svelte 5 component:
 *
 *   const autosave = createAutosave({
 *     get: () => editing,               // reactive read — re-runs on change
 *     save: async (snap) => db.put(snap),
 *     delayMs: 500,
 *   });
 *   $effect(autosave.watch);            // subscribes + returns cleanup
 *
 * Call `autosave.flush()` before the edited item is swapped out (e.g. switching
 * to a different template) so the pending debounce doesn't swallow changes.
 */
export interface AutosaveOptions<T> {
  /** Reactive getter — read here triggers Svelte dependency tracking. */
  get: () => T | null | undefined;
  /** Async save — receives a plain-object snapshot of T. */
  save: (snapshot: T) => Promise<void>;
  /** How long to wait after the last change before persisting. */
  delayMs?: number;
  /** Called when save state changes; useful for rendering a status pill. */
  onStatus?: (s: AutosaveStatus) => void;
}

export type AutosaveStatus = 'idle' | 'dirty' | 'saving' | 'saved' | 'error';

export interface Autosave {
  /** Wire this to a `$effect` — returns a cleanup function. */
  watch(): () => void;
  /** Force-save any pending change immediately (awaits completion). */
  flush(): Promise<void>;
}

export function createAutosave<T extends object>(opts: AutosaveOptions<T>): Autosave {
  const delayMs = opts.delayMs ?? 500;
  let timer: number | undefined;
  let lastSerialized: string | null = null;
  let inflight = false;

  function status(s: AutosaveStatus) {
    opts.onStatus?.(s);
  }

  async function doSave(payload: T) {
    if (inflight) return;
    inflight = true;
    status('saving');
    try {
      await opts.save(payload);
      status('saved');
    } catch (err) {
      status('error');
      console.error('[autosave] save failed', err);
    } finally {
      inflight = false;
    }
  }

  return {
    watch() {
      const current = opts.get();
      if (current == null) {
        // No active payload — cancel any pending save for the previous one.
        if (timer !== undefined) {
          clearTimeout(timer);
          timer = undefined;
        }
        lastSerialized = null;
        status('idle');
        return () => {};
      }

      // $state.snapshot lives on globalThis in Svelte 5 apps; this module
      // stays framework-agnostic by using a structured-clone fallback.
      const plain = structuredClone(current);
      const serialized = JSON.stringify(plain);
      if (serialized === lastSerialized) return () => {};

      lastSerialized = serialized;
      status('dirty');

      if (timer !== undefined) clearTimeout(timer);
      timer = setTimeout(() => {
        timer = undefined;
        void doSave(plain);
      }, delayMs) as unknown as number;

      return () => {
        // Cleanup runs when the dep changes before the debounce fires.
        // We deliberately don't cancel the timer here — we still want the
        // pending save to land.
      };
    },

    async flush() {
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
      const current = opts.get();
      if (current == null) return;
      const plain = structuredClone(current);
      const serialized = JSON.stringify(plain);
      if (serialized === lastSerialized && !inflight) return;
      lastSerialized = serialized;
      await doSave(plain);
    },
  };
}
