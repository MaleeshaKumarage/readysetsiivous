/** A readable message for a failed request. Browser network failures ("Failed to fetch") become a plain sentence. */
export function errorMessage(failure: unknown, fallback: string): string {
  if (failure instanceof TypeError && /fetch|network/i.test(failure.message)) return 'Could not reach the server. Check your connection and try again.';
  if (failure instanceof Error && failure.message) return failure.message;
  return fallback;
}
