// Local-date helpers for planning. Dates are stored as 'YYYY-MM-DD' (local),
// so plain string comparison gives chronological order.

export function localDateStr(d: Date = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function addDaysStr(days: number, from: Date = new Date()): string {
  const d = new Date(from);
  d.setDate(d.getDate() + days);
  return localDateStr(d);
}

/** Format a 'YYYY-MM-DD' string for display, parsed as local midnight. */
export function formatPlanned(s: string): string {
  const d = new Date(`${s}T00:00`);
  return Number.isNaN(d.getTime())
    ? s
    : d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}
