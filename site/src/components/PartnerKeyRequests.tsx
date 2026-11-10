'use client';

import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { Badge, Button, Card, Field, Input, Textarea } from '@/ds';
import type { Branch } from '@/lib/adminApi';
import { partnerApi, type KeyRequest, type KeyRequestList } from '@/lib/partnerApi';
import { partnerCopy, partnerStatus, type PartnerLanguage } from '@/lib/partnerLocale';
import { KeyRequestPhotos } from '@/components/KeyRequestPhotos';

type Copy = Record<keyof typeof partnerCopy.en, string>;

// Partner key pickup and handover requests. Each request keeps the fee it was submitted with.
export function PartnerKeyRequests({ t, language, branches, money, date }: {
  t: Copy; language: PartnerLanguage; branches: Branch[]; money: (value?: number | null) => string; date: (value: string) => string;
}) {
  const [list, setList] = useState<KeyRequestList | null>(null);
  const [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false);
  const [branchId, setBranchId] = useState(''), [kind, setKind] = useState<'Pickup' | 'Handover'>('Pickup'), [day, setDay] = useState(''), [notes, setNotes] = useState('');
  // Reused until a request succeeds, so a retry after a lost response cannot create a second request.
  const [clientRequestId, setClientRequestId] = useState(() => crypto.randomUUID());
  const [cancelling, setCancelling] = useState<string | null>(null), [cancelReason, setCancelReason] = useState('');
  const activeBranches = useMemo(() => branches.filter(branch => branch.isActive), [branches]);
  const selectedBranch = activeBranches.some(branch => branch.id === branchId) ? branchId : activeBranches[0]?.id ?? '';

  const refresh = useCallback(async () => {
    try { setList(await partnerApi.keyRequests()); } catch (e) { setError(e instanceof Error ? e.message : ''); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !selectedBranch || !day) return;
    setBusy(true); setError(''); setNotice('');
    try {
      await partnerApi.createKeyRequest({ clientRequestId, branchId: selectedBranch, kind, date: day, notes: notes.trim() || null });
      setNotice(t.keySaved); setKind('Pickup'); setDay(''); setNotes(''); setClientRequestId(crypto.randomUUID());
    } catch (e) { setError(e instanceof Error ? e.message : ''); }
    finally { setBusy(false); await refresh(); }
  }

  async function cancel(request: KeyRequest) {
    const reason = cancelReason.trim();
    if (busy || !reason) return;
    setBusy(true); setError(''); setNotice('');
    try {
      await partnerApi.cancelKeyRequest(request, reason);
      setNotice(t.keyCancelled); setCancelling(null); setCancelReason('');
    } catch (e) { setError(e instanceof Error ? e.message : ''); }
    finally { setBusy(false); await refresh(); }
  }

  const items = list?.items ?? [];
  const kindLabel = (value: string) => value === 'Handover' ? t.keyHandover : t.keyPickup;

  return <>
    <div className="partner-section-heading"><div><h2>{t.keyRequests}</h2><p>{t.keyRequestsHint}</p></div></div>
    {error && <p role="alert" className="partner-alert partner-alert-error">{error}</p>}
    {notice && <p role="status" className="partner-alert">{notice}</p>}
    {!activeBranches.length ? <Card className="partner-surface partner-empty">{t.keyNoProperties}</Card> : <Card className="partner-surface">
      <form onSubmit={submit} aria-label={t.newKeyRequest} className="grid gap-3">
        <h3>{t.newKeyRequest}</h3>
        <Field label={t.property}><select className="partner-field partner-select" value={selectedBranch} onChange={event => setBranchId(event.target.value)}>
          {activeBranches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}
        </select></Field>
        <fieldset className="partner-form-wide">
          <legend>{t.keyKind}</legend>
          <label><input type="radio" name="key-kind" value="Pickup" checked={kind === 'Pickup'} onChange={() => setKind('Pickup')} /> {t.keyPickup}</label>{' '}
          <label><input type="radio" name="key-kind" value="Handover" checked={kind === 'Handover'} onChange={() => setKind('Handover')} /> {t.keyHandover}</label>
        </fieldset>
        <Field label={t.keyDate}><Input className="partner-field" name="keyDate" type="date" required value={day} onChange={event => setDay(event.target.value)} /></Field>
        <Field label={t.keyNotes} className="partner-form-wide"><Textarea className="partner-field" maxLength={1000} value={notes} onChange={event => setNotes(event.target.value)} /></Field>
        <p className="partner-price">{t.keyFee} <strong>{list ? money(list.feeNet) : '…'}</strong> {t.exclVat} ({t.vat} {list?.vatRatePercent ?? ''}%)</p>
        <div className="partner-card-actions"><Button type="submit" disabled={busy || !day || !selectedBranch}>{t.submitKeyRequest}</Button></div>
      </form>
    </Card>}

    {!items.length ? <Card className="partner-surface partner-empty">{t.noKeyRequests}</Card> : <div className="partner-grid">{items.map(request => <article key={request.id} aria-label={request.branchName + ' ' + request.date}>
      <Card className="partner-surface partner-request">
        <div className="partner-card-heading"><h3>{request.branchName}</h3><Badge variant="outline" className="partner-badge">{partnerStatus(request.status, language)}</Badge></div>
        <p className="partner-request-window">{kindLabel(request.kind)} · {date(request.date)}</p>
        <p className="partner-price">{t.keyRequestFee} <strong>{money(request.feeNet)}</strong> {t.exclVat} ({t.vat} {request.vatRatePercent}%)</p>
        {request.notes && <p className="partner-entered">{request.notes}</p>}
        {(request.photos?.length ?? 0) > 0 && <KeyRequestPhotos heading={t.completionPhotos} alt={t.completionPhoto} photos={request.photos ?? []} loadUrl={photoId => partnerApi.keyRequestPhotoUrl(request.id, photoId)} />}
        {request.status === 'Cancelled' && request.cancellationReason && <p className="partner-entered">{request.cancellationReason}</p>}
        {request.status !== 'Cancelled' && (cancelling === request.id ? <div role="group" aria-label={t.cancelKeyRequest} className="partner-confirm">
          <Field label={t.keyCancelReason} className="partner-form-wide"><Textarea className="partner-field" required maxLength={1000} value={cancelReason} onChange={event => setCancelReason(event.target.value)} /></Field>
          <div className="partner-card-actions"><Button variant="ghost" className="partner-danger" disabled={busy || !cancelReason.trim()} onClick={() => void cancel(request)}>{t.confirmKeyCancel}</Button><Button variant="outline" className="partner-secondary" disabled={busy} onClick={() => { setCancelling(null); setCancelReason(''); }}>{t.keepKeyRequest}</Button></div>
        </div> : <div className="partner-card-actions"><Button variant="ghost" className="partner-danger" disabled={busy} onClick={() => { setCancelReason(''); setCancelling(request.id); }}>{t.cancelKeyRequest}</Button></div>)}
      </Card>
    </article>)}</div>}
  </>;
}
