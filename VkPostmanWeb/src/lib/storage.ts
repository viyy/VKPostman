// Thin wrappers around the Storage API for the header quota indicator.

export interface StorageInfo {
  usage: number;
  quota: number;
  persisted: boolean;
}

export async function getStorageInfo(): Promise<StorageInfo | null> {
  if (!navigator.storage?.estimate) return null;
  const est = await navigator.storage.estimate();
  let persisted = false;
  try {
    persisted = (await navigator.storage.persisted?.()) ?? false;
  } catch {
    /* not supported — leave false */
  }
  return { usage: est.usage ?? 0, quota: est.quota ?? 0, persisted };
}

/** Ask the browser to keep our data from being evicted under storage pressure. */
export async function requestPersistentStorage(): Promise<boolean> {
  try {
    return (await navigator.storage?.persist?.()) ?? false;
  } catch {
    return false;
  }
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`;
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}
