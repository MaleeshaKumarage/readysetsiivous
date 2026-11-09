'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Bell, ChevronDown } from 'lucide-react';
import { Button, Card, Textarea } from '@/ds';
import { inventoryAcknowledgments, type AcknowledgmentPage, type InventoryAcknowledgment } from '@/lib/inventoryAcknowledgmentApi';
import { InventoryEvidence } from './InventoryEvidence';

const copy = {
  en: {
    title: 'Inventory acknowledgments', needs: 'Needs acknowledgment', history: 'Response history', pending: 'Pending', review: 'Review entry', close: 'Close entry',
    empty: 'No inventory entries need your acknowledgment.', noHistory: 'No responses yet.', refresh: 'Refresh acknowledgments', loading: 'Loading acknowledgments…',
    failure: 'Acknowledgments could not be loaded. Refresh to try again.', responseFailure: 'The response could not be confirmed. Refresh to check whether it was saved before retrying.',
    acknowledge: 'Acknowledge entry', issue: 'Report an issue', note: 'Response note (required for an issue)', required: 'Describe the issue before sending.',
    acknowledged: 'Acknowledged', reported: 'Issue reported', saved: 'Your response was recorded.', from: 'Message from admin', recorded: 'Recorded', requested: 'Requested',
    previous: 'Previous', next: 'Next', retention: 'Photos are retained for three months. Acknowledgment confirms you reviewed this inventory entry.', cancel: 'Cancel', sendIssue: 'Send issue',
  },
  fi: {
    title: 'Varastokirjausten kuittaukset', needs: 'Odottaa kuittausta', history: 'Vastaushistoria', pending: 'Odottavat', review: 'Tarkista kirjaus', close: 'Sulje kirjaus',
    empty: 'Ei kuittausta odottavia varastokirjauksia.', noHistory: 'Ei vielä vastauksia.', refresh: 'Päivitä kuittaukset', loading: 'Ladataan kuittauksia…',
    failure: 'Kuittauksia ei voitu ladata. Yritä päivittää uudelleen.', responseFailure: 'Vastausta ei voitu vahvistaa. Päivitä ja tarkista, tallentuiko vastaus, ennen kuin yrität uudelleen.',
    acknowledge: 'Kuittaa kirjaus', issue: 'Ilmoita ongelmasta', note: 'Vastaus (pakollinen ongelmailmoituksessa)', required: 'Kuvaile ongelma ennen lähettämistä.',
    acknowledged: 'Kuitattu', reported: 'Ongelma ilmoitettu', saved: 'Vastauksesi on tallennettu.', from: 'Ylläpitäjän viesti', recorded: 'Kirjattu', requested: 'Pyydetty',
    previous: 'Edellinen', next: 'Seuraava', retention: 'Kuvat säilytetään kolme kuukautta. Kuittaus vahvistaa, että olet tarkistanut varastokirjauksen.', cancel: 'Peruuta', sendIssue: 'Lähetä ongelmailmoitus',
  },
};

export function PartnerInventoryAcknowledgments({ language, refreshToken }: { language: 'fi' | 'en'; refreshToken: number }) {
  const t = copy[language];
  const [data, setData] = useState<AcknowledgmentPage | null>(null), [history, setHistory] = useState(false), [page, setPage] = useState(0);
  const [expanded, setExpanded] = useState(false), [selected, setSelected] = useState<string | null>(null);
  const [loading, setLoading] = useState(true), [saving, setSaving] = useState(false), [failed, setFailed] = useState(false);
  const [responseFailed, setResponseFailed] = useState(false), [notice, setNotice] = useState(false), [note, setNote] = useState(''), [issue, setIssue] = useState(false), [required, setRequired] = useState(false);
  const [reload, setReload] = useState(0);
  const generation = useRef(0), mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const refresh = useCallback(() => setReload(value => value + 1), []);
  useEffect(() => {
    const attempt = ++generation.current, controller = new AbortController();
    setLoading(true); setFailed(false); setResponseFailed(false);
    inventoryAcknowledgments.list(history, page * 25, controller.signal).then(result => {
      if (attempt === generation.current) setData(result);
    }).catch(() => { if (!controller.signal.aborted && attempt === generation.current) setFailed(true); })
      .finally(() => { if (!controller.signal.aborted && attempt === generation.current) setLoading(false); });
    return () => controller.abort();
  }, [history, page, reload, refreshToken]);
  function open(row: InventoryAcknowledgment) { setSelected(selected === row.id ? null : row.id); setNote(''); setIssue(false); setRequired(false); setNotice(false); }
  async function respond(row: InventoryAcknowledgment, status: 'Acknowledged' | 'IssueReported') {
    if (saving || loading || failed || responseFailed) return;
    if (status === 'IssueReported' && !note.trim()) { setRequired(true); return; }
    setSaving(true); setNotice(false);
    try {
      await inventoryAcknowledgments.respond(row, status, note);
      if (!mounted.current) return;
      setSelected(null); setNote(''); setIssue(false); setNotice(true);
      setData(previous => previous ? { ...previous, items: previous.items.filter(item => item.id !== row.id), pendingCount: Math.max(0, previous.pendingCount - 1) } : null);
      setPage(0); refresh();
    } catch { if (mounted.current) setResponseFailed(true); }
    finally { if (mounted.current) setSaving(false); }
  }
  function switchView(next: boolean) { setHistory(next); setPage(0); setSelected(null); }
  const stamp = (value: string) => new Intl.DateTimeFormat(language === 'fi' ? 'fi-FI' : 'en-GB', { timeZone: 'Europe/Helsinki', dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  return <section aria-label={t.title} className="partner-acknowledgments">
    <Card className="partner-surface">
      <div className="partner-card-heading">
        <Button variant="ghost" onClick={() => setExpanded(value => !value)} aria-expanded={expanded} aria-controls="inventory-acknowledgment-inbox"><Bell size={18} aria-hidden="true" />
          {data && data.pendingCount > 0 ? `${t.needs} (${data.pendingCount})` : t.title}<ChevronDown size={16} aria-hidden="true" /></Button>
        <Button variant="ghost" size="sm" disabled={saving || loading} onClick={refresh}>{t.refresh}</Button>
      </div>
      {loading && <p role="status">{t.loading}</p>}
      {(failed || responseFailed) && <p role="alert" className="partner-alert partner-alert-error">{responseFailed ? t.responseFailure : t.failure}</p>}
      {notice && <p role="status" className="partner-alert partner-alert-success">{t.saved}</p>}
      {expanded && <div id="inventory-acknowledgment-inbox">
        <div className="partner-card-actions"><Button variant={history ? 'ghost' : 'outline'} aria-pressed={!history} disabled={saving} onClick={() => switchView(false)}>{t.pending}</Button>
          <Button variant={history ? 'outline' : 'ghost'} aria-pressed={history} disabled={saving} onClick={() => switchView(true)}>{t.history}</Button></div>
        {!loading && !failed && data?.items.length === 0 && <p>{history ? t.noHistory : t.empty}</p>}
        {!loading && !failed && data?.items.map(row => <article key={row.id} className="partner-acknowledgment-entry" aria-label={`${row.itemName} ${row.locationName}`}>
          <h3>{row.itemName} · {row.quantity} {row.unit}</h3>
          <p>{row.locationName} · {t.recorded}: {stamp(row.takenUtc)}</p>
          <p className="partner-muted">{t.requested}: {stamp(row.createdUtc)}</p>
          {row.status !== 'Pending' && <p><strong>{row.status === 'Acknowledged' ? t.acknowledged : t.reported}</strong> · {row.respondedByName} · {row.respondedUtc && stamp(row.respondedUtc)}</p>}
          <Button variant="outline" disabled={saving} aria-expanded={selected === row.id} onClick={() => open(row)}>{selected === row.id ? t.close : t.review}</Button>
          {selected === row.id && <div className="partner-acknowledgment-detail">
            {row.note && <p style={{ whiteSpace: 'pre-wrap' }}>{row.note}</p>}
            {row.message && <p style={{ whiteSpace: 'pre-wrap' }}><strong>{t.from}:</strong> {row.message}</p>}
            <InventoryEvidence paths={row.photoIds.map(id => inventoryAcknowledgments.photoPath(row.id, id))} itemName={row.itemName} language={language} />
            <p className="partner-muted">{t.retention}</p>
            {row.status === 'Pending' ? <>
              <label className="block mb-1.5 text-sm font-semibold" htmlFor={`ack-note-${row.id}`}>{t.note}</label>
              <Textarea id={`ack-note-${row.id}`} maxLength={500} value={note} disabled={saving} onChange={event => { setNote(event.target.value); setRequired(false); }} />
              {required && <p role="alert">{t.required}</p>}
              <div className="partner-card-actions">
                {!issue ? <><Button disabled={saving || loading || failed || responseFailed} onClick={() => void respond(row, 'Acknowledged')}>{t.acknowledge}</Button>
                  <Button variant="outline" disabled={saving || responseFailed} onClick={() => setIssue(true)}>{t.issue}</Button></>
                  : <><Button disabled={saving || responseFailed} onClick={() => void respond(row, 'IssueReported')}>{t.sendIssue}</Button><Button variant="ghost" disabled={saving} onClick={() => setIssue(false)}>{t.cancel}</Button></>}
              </div>
            </> : row.responseNote && <p style={{ whiteSpace: 'pre-wrap' }}>{row.responseNote}</p>}
          </div>}
        </article>)}
        <div className="partner-card-actions">
          {page > 0 && <Button variant="outline" disabled={loading || saving} onClick={() => { setPage(value => value - 1); setSelected(null); }}>{t.previous}</Button>}
          {data?.hasMore && <Button variant="outline" disabled={loading || saving} onClick={() => { setPage(value => value + 1); setSelected(null); }}>{t.next}</Button>}
        </div>
      </div>}
    </Card>
  </section>;
}
