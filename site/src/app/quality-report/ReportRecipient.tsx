'use client';

import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { API_URL } from '@/lib/api';
import { Logo } from '@/ds/Logo';

const copy = {
  en: { incomplete: 'incomplete timing records excluded', title: 'Cleaning Quality Report', company: 'Company', branch: 'Branch', shift: 'Shift', scheduled: 'Scheduled occurrences', completed: 'Completed occurrences', plannedHours: 'Scheduled hours', actualHours: 'Actual staff hours', pdf: 'Download PDF', ack: 'Acknowledge receipt', done: 'Receipt acknowledged', error: 'This report is unavailable, expired or revoked.', retry: 'Retry', unavailable: 'Not available', expires: 'Link expires', notice: 'Acknowledgment confirms receipt of this report. It is not a signature or approval of its contents.', failed: 'Could not acknowledge receipt. Please retry.' },
  fi: { incomplete: 'puutteellista aikakirjausta jätetty pois', title: 'Siivouksen laaturaportti', company: 'Yritys', branch: 'Toimipiste', shift: 'Vuoro', scheduled: 'Suunnitellut käynnit', completed: 'Kirjatut käynnit', plannedHours: 'Suunnitellut tunnit', actualHours: 'Toteutuneet työtunnit', pdf: 'Lataa PDF', ack: 'Kuittaa vastaanotto', done: 'Vastaanotto kuitattu', error: 'Raportti ei ole saatavilla, linkki on vanhentunut tai peruutettu.', retry: 'Yritä uudelleen', unavailable: 'Ei saatavilla', expires: 'Linkki vanhenee', notice: 'Kuittaus vahvistaa raportin vastaanoton. Se ei ole allekirjoitus eikä sisällön hyväksyntä.', failed: 'Vastaanoton kuittaus epäonnistui. Yritä uudelleen.' },
  sv: { incomplete: 'ofullständiga tidsregistreringar har uteslutits', title: 'Kvalitetsrapport för städning', company: 'Företag', branch: 'Verksamhetsställe', shift: 'Arbetspass', scheduled: 'Planerade besök', completed: 'Registrerade besök', plannedHours: 'Planerade timmar', actualHours: 'Faktiska personaltimmar', pdf: 'Ladda ner PDF', ack: 'Bekräfta mottagandet', done: 'Mottagandet bekräftat', error: 'Rapporten är inte tillgänglig, länken har upphört att gälla eller återkallats.', retry: 'Försök igen', unavailable: 'Ej tillgängligt', expires: 'Länken upphör att gälla', notice: 'Bekräftelsen gäller mottagandet av rapporten. Den är inte en underskrift eller ett godkännande av innehållet.', failed: 'Mottagandet kunde inte bekräftas. Försök igen.' },
};
interface PublicReport { language: string; expiresUtc: string; acknowledgedUtc?: string | null; year: number; month: number; periods?: { from: string; to: string }[];
  shifts: { companyName: string; branchName: string; shiftName: string; scheduledOccurrences: number; completedOccurrences: number; scheduledHours: number; actualHours?: number | null; incompleteTimingRecords?: number | null }[]; }
const dateLabel = (date: string) => date.split('-').reverse().join('/');

export default function ReportRecipient() {
  const search = useSearchParams(); const tenant = search.get('tenant') ?? ''; const token = search.get('token') ?? '';
  const base = `${API_URL}/api/v1/public/${encodeURIComponent(tenant)}/quality-cycle/reports/${encodeURIComponent(token)}`;
  const [report, setReport] = useState<PublicReport | null>(null); const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false); const [ackError, setAckError] = useState(false); const [busy, setBusy] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const lang = report?.language === 'fi' || report?.language === 'sv' ? report.language : 'en'; const t = copy[lang];
  useEffect(() => {
    let active = true; setReport(null); setLoading(true); setError(false);
    async function load() {
      try { if (!tenant || !token) throw new Error(); const response = await fetch(base, { cache: 'no-store', referrerPolicy: 'no-referrer' }); if (!response.ok) throw new Error(); const data = await response.json(); if (active) setReport(data); }
      catch { if (active) setError(true); } finally { if (active) setLoading(false); }
    }
    void load(); return () => { active = false; };
  }, [base, tenant, token, attempt]);
  async function acknowledge() {
    setBusy(true); setAckError(false);
    try { const response = await fetch(`${base}/acknowledge`, { method: 'POST', referrerPolicy: 'no-referrer' }); if (!response.ok) throw new Error(); setReport(await response.json()); }
    catch { setAckError(true); } finally { setBusy(false); }
  }
  return <main lang={lang} className="min-h-screen bg-gray-50 px-4 py-10 text-gray-900 dark:bg-gray-950 dark:text-gray-100">
    <article className="mx-auto max-w-6xl space-y-6 rounded-2xl border border-gray-200 bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-900 sm:p-8">
      <Logo className="h-12 w-auto" /><h1 className="text-2xl font-bold">{t.title}</h1>
      {loading && <p role="status">Loading…</p>}
      {error && <div role="alert"><p>{t.error}</p><button className="mt-3 rounded border px-4 py-2" onClick={() => setAttempt(value => value + 1)}>{t.retry}</button></div>}
      {report && <>
        <p>{report.periods?.map(p => `${dateLabel(p.from)} – ${dateLabel(p.to)}`).join('; ') ?? `${report.year}-${String(report.month).padStart(2, '0')}`}</p>
        <div className="overflow-x-auto"><table className="w-full min-w-[850px] text-left text-sm"><thead><tr>{[t.company, t.branch, t.shift, t.scheduled, t.completed, t.plannedHours, t.actualHours].map(label => <th className="border-b p-2" key={label}>{label}</th>)}</tr></thead>
          <tbody>{report.shifts.map((shift, index) => <tr key={index}>{[shift.companyName, shift.branchName, shift.shiftName, shift.scheduledOccurrences, shift.completedOccurrences, shift.scheduledHours.toFixed(2), shift.actualHours == null ? t.unavailable : shift.actualHours.toFixed(2) + ((shift.incompleteTimingRecords ?? 0) > 0 ? ' (' + shift.incompleteTimingRecords + ' ' + t.incomplete + ')' : '')].map((value, column) => <td key={column} className="border-b p-2">{value}</td>)}</tr>)}</tbody></table></div>
        <a className="inline-flex rounded bg-[#0a1741] px-4 py-2 font-semibold text-white" href={`${base}/pdf`} target="_blank" rel="noreferrer">{t.pdf}</a>
        <p className="text-sm">{t.expires}: {new Date(report.expiresUtc).toLocaleDateString(lang)}</p>
        <p className="text-sm">{t.notice}</p>
        {report.acknowledgedUtc ? <p role="status" className="font-semibold text-green-700 dark:text-green-400">{t.done}: {new Date(report.acknowledgedUtc).toLocaleString(lang)}</p>
          : <button disabled={busy} className="rounded bg-[#ddc067] px-4 py-2 font-semibold text-gray-950 disabled:opacity-50" onClick={acknowledge}>{t.ack}</button>}
        {ackError && <p role="alert" className="text-red-600">{t.failed}</p>}
      </>}
    </article>
  </main>;
}
