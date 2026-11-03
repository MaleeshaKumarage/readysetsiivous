/** Postal codes stay strings so leading zeros remain significant. Missing codes sort last. */
export function compareBranchesByPostalCode(a: { postalCode?: string | null; name?: string; id: string }, b: { postalCode?: string | null; name?: string; id: string }) {
  const normalize = (value?: string | null) => (value ?? '').trim().replace(/\s+/g, '').toUpperCase();
  const first = normalize(a.postalCode), second = normalize(b.postalCode);
  if (!first !== !second) return first ? -1 : 1;
  return first.localeCompare(second, 'en', { numeric: false }) || (a.name ?? '').localeCompare(b.name ?? '') || a.id.localeCompare(b.id);
}
