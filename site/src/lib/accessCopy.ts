// Shown when the admin or manager area cannot verify the signed-in account. Shared so both areas say the same thing.
export const ACCESS_VERIFY_ERROR =
  'Could not verify your access. The server could not be reached or returned an error, so nothing was changed. Check your connection and retry. If it keeps failing, contact your tenant administrator.';
export const ACCESS_RETRY_LABEL = 'Retry verification';

export class AccessVerificationError extends Error {}

export async function verifyIdentity(url: string, bearer: string): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(url, { headers: { Authorization: `Bearer ${bearer}` }, cache: 'no-store', signal: AbortSignal.timeout(10000) });
  } catch {
    throw new AccessVerificationError('The access server could not be reached. Check your connection and retry. Support reference: ACCESS-NETWORK.');
  }
  if (!response.ok && ![401, 403].includes(response.status)) {
    throw new AccessVerificationError(`The access server returned HTTP ${response.status}. Retry shortly. Support reference: ACCESS-HTTP-${response.status}.`);
  }
  return response;
}
