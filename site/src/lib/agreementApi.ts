import { API_URL } from './api';

const TENANT = process.env.NEXT_PUBLIC_TENANT_SLUG ?? 'readysetsiivous';

export interface PublicAgreementDto {
  title: string; status: string; totalSigners: number; signedCount: number; completed: boolean; signed: boolean;
}

export async function getPublicAgreement(token: string): Promise<PublicAgreementDto | null> {
  try {
    const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}`, {
      signal: AbortSignal.timeout(8000),
    });
    return r.ok ? await r.json() : null;
  } catch {
    return null;
  }
}

export interface SignResult {
  ok: boolean;
  message?: string;
}

async function errorMessage(r: Response): Promise<string> {
  try {
    const body = (await r.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> };
    const first = body.errors ? Object.values(body.errors).flat()[0] : undefined;
    return first ?? body.detail ?? body.title ?? `Request failed (${r.status})`;
  } catch {
    return `Request failed (${r.status})`;
  }
}

export async function signAgreement(token: string, typedName: string, signaturePng: string): Promise<SignResult> {
  try {
    const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}/sign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ typedName, signaturePng }),
      signal: AbortSignal.timeout(30000),
    });
    if (r.ok) return { ok: true };
    return { ok: false, message: await friendlyError(r) };
  } catch {
    return { ok: false, message: 'Network error — check your connection and try again' };
  }
}

async function friendlyError(r: Response): Promise<string> {
  const raw = await errorMessage(r);
  if (r.status === 404) return 'This signing link is invalid or expired. Contact the sender for a new link.';
  if (r.status === 409) {
    if (/already signed/i.test(raw)) return 'You have already signed this agreement.';
    if (/cancelled|canceled/i.test(raw)) return 'This agreement has been cancelled.';
    return 'This agreement changed while you were signing. Please try again.';
  }
  if (r.status === 400) return raw;
  if (r.status === 500) return 'Something went wrong on our side. Please try again shortly.';
  return raw;
}

export const agreementFileUrl = (token: string) =>
  `${API_URL}/api/v1/public/${TENANT}/agreements/${token}/file`;
