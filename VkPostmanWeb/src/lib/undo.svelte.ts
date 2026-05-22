// A tiny shared "soft delete" buffer. A delete handler removes the row
// immediately and calls `undo.offer(...)`; App.svelte shows a toast for a few
// seconds with an Undo button that runs the supplied restore callback.

const UNDO_WINDOW_MS = 5000;

class UndoStore {
  /** Toast text while an undo is available, else null. */
  message = $state<string | null>(null);

  #restore: (() => Promise<void> | void) | null = null;
  #timer: ReturnType<typeof setTimeout> | undefined;

  /** Offer an undo for `message`; auto-dismisses after the window elapses. */
  offer(message: string, restore: () => Promise<void> | void): void {
    clearTimeout(this.#timer);
    this.message = message;
    this.#restore = restore;
    this.#timer = setTimeout(() => this.dismiss(), UNDO_WINDOW_MS);
  }

  async undo(): Promise<void> {
    clearTimeout(this.#timer);
    const restore = this.#restore;
    this.#restore = null;
    this.message = null;
    if (restore) await restore();
  }

  dismiss(): void {
    clearTimeout(this.#timer);
    this.#restore = null;
    this.message = null;
  }
}

export const undo = new UndoStore();
