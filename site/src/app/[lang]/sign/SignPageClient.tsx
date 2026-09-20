'use client';

import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import SignaturePad, { type SignaturePadHandle } from '@/components/admin/SignaturePad';
import { getPublicAgreement, signAgreement, agreementFileUrl, type PublicAgreementDto } from '@/lib/agreementApi';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

type Status = 'loading' | 'ready' | 'invalid' | 'cancelled' | 'done';

export default function SignPageClient() {
  const token = useSearchParams().get('token') ?? '';
  const padRef = useRef<SignaturePadHandle>(null);
  const [agreement, setAgreement] = useState<PublicAgreementDto | null>(null);
  const [status, setStatus] = useState<Status>('loading');
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [fileKey, setFileKey] = useState(0);

  useEffect(() => {
    getPublicAgreement(token).then((a) => {
      if (!a) { setStatus('invalid'); return; }
      setAgreement(a);
      if (a.status === 'Cancelled') setStatus('cancelled');
      else if (a.completed) { setStatus('done'); setFileKey(Date.now()); }
      else setStatus('ready');
    });
  }, [token]);

  async function sign() {
    setError(null);
    if (!name.trim()) { setError('Please enter your full name.'); return; }
    const signature = padRef.current?.getPng() ?? '';
    if (!signature) { setError('Please draw or type your signature first.'); return; }
    setLoading(true);
    const result = await signAgreement(token, name, signature);
    setLoading(false);
    if (result.ok) {
      setStatus('done');
      // Re-fetch to learn whether everyone has now signed; refresh the preview if so.
      const a = await getPublicAgreement(token);
      if (a) { setAgreement(a); if (a.completed) setFileKey(Date.now()); }
    } else {
      setError(result.message ?? 'Signing failed. Please try again.');
    }
  }

  const fileUrl = fileKey ? `${agreementFileUrl(token)}?cb=${fileKey}` : agreementFileUrl(token);

  if (status === 'loading') {
    return <div className="mx-auto max-w-2xl p-6 text-center text-muted-foreground">Loading…</div>;
  }

  if (status === 'invalid') {
    return (
      <div className="mx-auto max-w-2xl space-y-3 p-6">
        <h1 className="text-2xl font-semibold">Link not found</h1>
        <p className="text-sm text-muted-foreground">
          This signing link is invalid or has expired. Contact the person who sent it to get a new link.
        </p>
      </div>
    );
  }

  if (status === 'cancelled') {
    return (
      <div className="mx-auto max-w-2xl space-y-3 p-6">
        <h1 className="text-2xl font-semibold">{agreement?.title ?? 'Agreement'}</h1>
        <p className="text-sm text-muted-foreground">This agreement has been cancelled and is no longer open for signing.</p>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6 p-6">
      <h1 className="text-2xl font-semibold">{agreement?.title ?? 'Agreement'}</h1>
      {agreement && (
        <p className="text-sm text-muted-foreground">
          {agreement.signedCount}/{agreement.totalSigners} signed
          {status === 'done' && ' — complete'}
        </p>
      )}

      <iframe src={fileUrl} className="h-[60vh] w-full rounded-md border" />

      {status === 'done' ? (
        <div className="space-y-3 rounded-xl border p-4">
          <p className="font-medium">You have signed this agreement.</p>
          {agreement?.completed ? (
            <>
              <p className="text-sm text-muted-foreground">Everyone has signed. Download the completed document below.</p>
              <Button onClick={() => window.open(fileUrl)} className="w-full">Download signed document</Button>
            </>
          ) : (
            <p className="text-sm text-muted-foreground">
              Waiting for the remaining signers. You can return to this link to download the completed document once everyone has signed.
            </p>
          )}
        </div>
      ) : (
        <div className="space-y-4 rounded-xl border p-4">
          <div className="space-y-1.5">
            <Label>Full name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Your full name" />
          </div>
          <div className="space-y-1.5">
            <Label>Signature</Label>
            <p className="text-xs text-muted-foreground">Draw with your mouse/finger, or switch to Type and write your name.</p>
            <SignaturePad ref={padRef} />
          </div>
          {error && (
            <p className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">{error}</p>
          )}
          <Button onClick={sign} disabled={loading} className="w-full">
            {loading ? 'Signing…' : 'Sign'}
          </Button>
        </div>
      )}
    </div>
  );
}
