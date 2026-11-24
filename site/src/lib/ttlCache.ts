// Small in-memory cache for public, non-sensitive reads. Entries live for ttlMs, concurrent loads for the
// same key share one request, and empty results or failures are never cached so a retry can succeed.
export function createTtlCache<T>(ttlMs: number, now: () => number = Date.now) {
  const values = new Map<string, { value: T; expires: number }>();
  const pending = new Map<string, Promise<T | null>>();

  return {
    getOrLoad(key: string, load: () => Promise<T | null>): Promise<T | null> {
      const hit = values.get(key);
      if (hit && hit.expires > now()) return Promise.resolve(hit.value);
      const inFlight = pending.get(key);
      if (inFlight) return inFlight;
      const request = load()
        .then(value => {
          if (value !== null) values.set(key, { value, expires: now() + ttlMs });
          return value;
        })
        .finally(() => pending.delete(key));
      pending.set(key, request);
      return request;
    },
    clear() {
      values.clear();
      pending.clear();
    },
  };
}
