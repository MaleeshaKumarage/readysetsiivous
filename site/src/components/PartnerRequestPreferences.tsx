'use client';
import { useEffect, useState } from 'react';
import { partnerSend } from '@/lib/partnerApi';
export function PartnerRequestPreferences() {
  const [enabled, setEnabled] = useState(false), [loaded, setLoaded] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState('');
  useEffect(() => { let active = true; partnerSend<{autoAcceptPartnerRequests: boolean}>('me/partner-request-preferences').then(value => { if (active) { setEnabled(value.autoAcceptPartnerRequests); setLoaded(true); } }).catch(error => { if (active) setError(error.message); }); return () => { active = false; }; }, []);
  async function save(value: boolean) {
    setBusy(true); setError(''); setNotice('');
    try { const saved = await partnerSend<{autoAcceptPartnerRequests: boolean}>('me/partner-request-preferences', 'PUT', { autoAcceptPartnerRequests: value }); setEnabled(saved.autoAcceptPartnerRequests); setNotice('Partner work preference saved.'); }
    catch (error) { setError(error instanceof Error ? error.message : 'Could not save preference.'); }
    finally { setBusy(false); }
  }
  return <section className="space-y-3" aria-busy={busy}><h2 className="text-xl font-medium">Partner work</h2><label className="flex min-h-11 items-center gap-3"><input type="checkbox" checked={enabled} disabled={!loaded || busy} onChange={event => void save(event.target.checked)} />Automatically accept partner work sent directly to me</label><p>Off by default. When disabled, you review and accept each offer in My shifts. Automatic acceptance needs a fixed appointment time; flexible requests still need your approval.</p>{!loaded && !error && <p role="status">Loading preference…</p>}{busy && <p role="status">Saving preference…</p>}{error && <p role="alert" className="text-red-700">{error}</p>}{notice && <p role="status">{notice}</p>}</section>;
}
