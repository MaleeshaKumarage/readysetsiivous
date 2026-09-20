import { API_URL } from './api';

const TENANT = process.env.NEXT_PUBLIC_TENANT_SLUG ?? 'readysetsiivous';

export interface PublicAgreementDto {
  title: string; status: string; totalSigners: number; signedCount: number; completed: boolean;
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

export async function signAgreement(token: string, typedName: string, signaturePng: string): Promise<boolean> {
  try {
    const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}/sign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ typedName, signaturePng }),
      signal: AbortSignal.timeout(30000),
    });
    return r.ok;
  } catch {
    return false;
  }
}

export const agreementFileUrl = (token: string) =>
  `${API_URL}/api/v1/public/${TENANT}/agreements/${token}/file`;
