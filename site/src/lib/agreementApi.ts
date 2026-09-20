import { API_URL } from './api';

const TENANT = process.env.NEXT_PUBLIC_TENANT_SLUG ?? 'readysetsiivous';

export interface PublicAgreementDto {
  title: string; status: string; totalSigners: number; signedCount: number; completed: boolean;
}

export async function getPublicAgreement(token: string): Promise<PublicAgreementDto | null> {
  const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}`);
  return r.ok ? await r.json() : null;
}

export async function signAgreement(token: string, typedName: string, signaturePng: string): Promise<boolean> {
  const r = await fetch(`${API_URL}/api/v1/public/${TENANT}/agreements/${token}/sign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ typedName, signaturePng }),
  });
  return r.ok;
}

export const agreementFileUrl = (token: string) =>
  `${API_URL}/api/v1/public/${TENANT}/agreements/${token}/file`;
