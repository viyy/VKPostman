/**
 * Debounced autosave helper.
 *
 * The `get()` closure must read reactive state (e.g. a Svelte 5 `$state`
 * variable) — every read that happens during `watch()` becomes a dependency,
 * so subsequent mutations re-run the watcher.
 *
 * Usage inside a Svelte 5 component:
 *
 *   const autosave = createAutosave({
 *     get: () => editing,
 *     save: async (snap) => db.put(snap),
 *     // Recommended: pass Svelte's own snapshot so nested proxies round-trip
 *     // cleanly to Dexie/IndexedDB. Falls back to structuredClone if omitted.
 *     snapshot: (v) => $state.snapshot(v),
 *     delayMs: 500,
 *   });
 *   $effect(autosave.watch);
 *
 * Call `autosave.flush()` before switching the edited record so the pending
 * debounce lands, and `autosave.reset()` right after switching so the new
 * record's current state becomes the baseline (no spurious save of an
 * untouched record).
 */

export type AutosaveStatus = 'idle' | 'dirty' | 'saving' | 'saved' | 'error';

export interface AutosaveOptions<T> {
  /** Reactive getter — re-runs on change. */
  get: () => T | null | undefined;
  /** Async save — receives a plain-object snapshot of T. */
  save: (snapshot: T) => Promise<void>;
  /** Snapshot function. Default: structuredClone. Pass `$state.snapshot` in Svelte 5. */
  snapshot?: (value: T) => T;
  /** How long to wait after the last change before persisting. */
  delayMs?: number;
  /** Called when save state changes; useful for rendering a status pill. */
  onStatus?: (s: AutosaveStatus) => void;
}

export interface Autosave {
  /** Wire this to a `$effect`; returns a cleanup function. */
  watch(): () => void;
  /** Force-save any pending change immediately (awaits completion). */
  flush(): Promise<void>;
  /** Adopt the current record's state as the baseline without saving. */
  reset(): void;
}

export function createAutosave<T extends object>(opts: AutosaveOptions<T>): Autosave {
  const delayMs = opts.delayMs ?? 500;
  const snapshot = opts.snapshot ?? ((v: T) => structuredClone(v));
  let timer: ReturnType<typeof setTimeout> | undefined;
  let lastSerialized: string | null = null;
  let saveQueued = false;
  let inflight = false;

  function status(s: AutosaveStatus) {
    opts.onStatus?.(s);
  }

  function take(current: T): { plain: T; serialized: string } | null {
    try {
      const plain = snapshot(current);
      const serialized = JSON.stringify(plain);
      return { plain, serialized };
    } catch (err) {
      console.error('[autosave] could not snapshot current value', err);
      return null;
    }
  }

  async function doSave(payload: T) {
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
      // Handle a change that came in while a save was in flight: snapshot
      // again and save once more so the latest edits aren't lost.
      if (saveQueued) {
        saveQueued = false;
        const next = opts.get();
        if (next == null) return;
        const taken = take(next);
        if (!taken) return;
        if (taken.serialized === lastSerialized) return;
        lastSerialized = taken.serialized;
        await doSave(taken.plain);
      }
    }
  }

  function schedule(taken: { plain: T; serialized: string }) {
    lastSerialized = taken.serialized;
    status('dirty');
    if (timer !== undefined) clearTimeout(timer);
    timer = setTimeout(() => {
      timer = undefined;
      if (inflight) {
        // Don't stack saves — mark that one more save is needed once the
        // current one finishes. The in-flight save's finally branch picks it up.
        saveQueued = true;
        return;
      }
      void doSave(taken.plain);
    }, delayMs);
  }

  return {
    watch() {
      const current = opts.get();
      if (current == null) {
        if (timer !== undefined) {
          clearTimeout(timer);
          timer = undefined;
        }
        lastSerialized = null;
        status('idle');
        return () => {};
      }

      const taken = take(current);
      if (!taken) return () => {};
      if (taken.serialized === lastSerialized) return () => {};

      schedule(taken);
      return () => {};
    },

    async flush() {
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
      const current = opts.get();
      if (current == null) return;
      const taken = take(current);
      if (!taken) return;
      if (taken.serialized === lastSerialized && !inflight) return;
      lastSerialized = taken.serialized;
      await doSave(taken.plain);
    },

    reset() {
      // Adopt the current snapshot as the baseline — next watch() tick will
      // find "no change" and skip scheduling a save. Use after opening a
      // record so just-looking-at-it doesn't churn the DB.
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
      const current = opts.get();
      if (current == null) {
        lastSerialized = null;
        status('idle');
        return;
      }
      const taken = take(current);
      lastSerialized = taken?.serialized ?? null;
      status('idle');
    },
  };
}
