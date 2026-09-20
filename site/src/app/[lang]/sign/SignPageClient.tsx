'use client';

import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import SignaturePad, { type SignaturePadHandle } from '@/components/admin/SignaturePad';
import { getPublicAgreement, signAgreement, agreementFileUrl, type PublicAgreementDto } from '@/lib/agreementApi';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export default function SignPageClient() {
  const token = useSearchParams().get('token') ?? '';
  const padRef = useRef<SignaturePadHandle>(null);
  const [agreement, setAgreement] = useState<PublicAgreementDto | null>(null);
  const [name, setName] = useState('');
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    getPublicAgreement(token).then((a) => { setAgreement(a); if (a?.completed) setDone(true); });
  }, [token]);

  async function sign() {
    if (!name.trim()) { setError('Enter your name'); return; }
    const signature = padRef.current?.getPng() ?? '';
    if (!signature) { setError('Enter your signature (draw or type)'); return; }
    setLoading(true);
    setError(null);
    try {
      const ok = await signAgreement(token, name, signature);
      if (ok) { setDone(true); }
      else setError('Signing failed — please try again');
    } catch {
      setError('Signing failed — please try again');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6 p-6">
      <h1 className="text-2xl font-semibold">{agreement?.title ?? 'Agreement'}</h1>
      {agreement && (
        <p className="text-sm text-muted-foreground">
          {agreement.signedCount}/{agreement.totalSigners} signed
          {agreement.completed && ' — complete'}
        </p>
      )}

      <iframe src={agreementFileUrl(token)} className="h-[60vh] w-full rounded-md border" />

      {done ? (
        <div className="space-y-3">
          <p className="font-medium">Signed. You can download the document below once everyone has signed.</p>
          {agreement?.completed && (
            <Button onClick={() => window.open(agreementFileUrl(token))}>Download signed document</Button>
          )}
        </div>
      ) : (
        <div className="space-y-4 rounded-xl border p-4">
          <div className="space-y-1.5">
            <Label>Full name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <SignaturePad ref={padRef} />
          {error && <p className="text-sm text-destructive">{error}</p>}
          <Button onClick={sign} disabled={loading} className="w-full">
            {loading ? 'Signing…' : 'Sign'}
          </Button>
        </div>
      )}
    </div>
  );
}
