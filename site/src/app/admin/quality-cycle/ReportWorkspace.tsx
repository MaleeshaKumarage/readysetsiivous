'use client';

import { DsGrid } from '@/components/ds/DsGrid';
import { showError } from '@/lib/toast';
import { useEffect, useRef, useState } from 'react';
import { Badge, Button, Group, Modal, MultiSelect, NumberInput, Paper, SegmentedControl, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { adminShifts, type Shift } from '@/lib/adminApi';
import { adminQualityCycle, reportLandingUrl, reportShareUrl, type MonthlyReport, type ReportSelection, type ReportShare } from '@/lib/qualityCycleApi';

const failure = (error: unknown) => error instanceof Error ? error.message : 'Report request failed. Please retry.';
const dateLabel = (value: string) => value.split('-').reverse().join('/');

export default function ReportWorkspace() {
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [shiftIds, setShiftIds] = useState<string[]>([]);
  const [mode, setMode] = useState('months');
  const [year, setYear] = useState<number | string>(new Date().getFullYear());
  const [months, setMonths] = useState([`${new Date().getFullYear()}-${String(new Date().getMonth() + 1).padStart(2, '0')}`]);
  const [from, setFrom] = useState(''); const [to, setTo] = useState('');
  const [language, setLanguage] = useState<'en' | 'fi' | 'sv'>('en');
  const [report, setReport] = useState<MonthlyReport | null>(null);
  const [loadedSelection, setLoadedSelection] = useState<ReportSelection | null>(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const [shares, setShares] = useState<ReportShare[]>([]);
  const [shareSelection, setShareSelection] = useState<ReportSelection | null>(null);
  const [recipients, setRecipients] = useState<{ label: string; email: string }[]>([]);
  const [recipientChoice, setRecipientChoice] = useState('other'); const [email, setEmail] = useState('');
  const [shareError, setShareError] = useState(''); const [sending, setSending] = useState(false);
  const version = useRef(0); const recipientVersion = useRef(0);
  const selection: ReportSelection = { shiftIds, language, ...(mode === 'months' ? { months } : { from, to }) };
  const valid = shiftIds.length > 0 && shiftIds.length <= 50 && (mode === 'months' ? months.length > 0 : !!from && !!to && from <= to);
  const periodKey = JSON.stringify({ shiftIds, mode, months, from, to });

  // Action failures are shown in the panel and as a toast, so they are noticed even when the panel is below the fold.
  const reportError = (message: string) => { setError(message); showError(message, 'Report action not completed'); };
  const reportShareError = (message: string) => { setShareError(message); showError(message, 'Report not shared'); };
  useEffect(() => {
    let active = true;
    void adminShifts.list(undefined, undefined, true).then(value => { if (active) { if (value) setShifts(value); else setError('Could not load shifts. Refresh this page to retry.'); } }).catch(e => { if (active) reportError(failure(e)); });
    return () => { active = false; version.current++; recipientVersion.current++; };
  }, []);
  useEffect(() => { version.current++; setReport(null); setLoadedSelection(null); setBusy(false); }, [periodKey]);

  const selectionYear = Number(year) >= 2000 && Number(year) <= 2100 && Number.isInteger(Number(year)) ? Number(year) : new Date().getFullYear();
  const monthOptions = [...new Set([...Array.from({ length: 12 }, (_, index) => `${selectionYear}-${String(index + 1).padStart(2, '0')}`), ...months])]
    .sort().map(value => ({ value, label: new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${value}-01T12:00:00Z`)) }));
  async function load() {
    const current = ++version.current; setBusy(true); setError(''); setReport(null); setLoadedSelection(null);
    try { const value = await adminQualityCycle.selectedReport(selection); if (current === version.current) { setReport(value); setLoadedSelection(selection); } }
    catch (e) { if (current === version.current) reportError(failure(e)); }
    finally { if (current === version.current) setBusy(false); }
  }
  async function download(id?: string) {
    if (!loadedSelection) return;
    setBusy(true); setError('');
    try { await adminQualityCycle.downloadSelectedPdf({ ...loadedSelection, language, ...(id ? { shiftIds: [id] } : {}) }); }
    catch (e) { reportError(failure(e)); } finally { setBusy(false); }
  }
  async function openShare(id?: string) {
    if (!loadedSelection) return;
    const chosen = { ...loadedSelection, language, ...(id ? { shiftIds: [id] } : {}) };
    const current = ++recipientVersion.current;
    setShareSelection(chosen); setEmail(''); setRecipientChoice('other'); setRecipients([]); setShareError('');
    try { const options = await adminQualityCycle.recipientOptions(chosen.shiftIds); if (current === recipientVersion.current) setRecipients(options); }
    catch (e) { if (current === recipientVersion.current) reportShareError(failure(e)); }
  }
  async function send() {
    if (!shareSelection) return;
    setSending(true); setShareError('');
    try {
      const created = await adminQualityCycle.sendReport(shareSelection, email);
      setShares(current => [created, ...current]);
      if (created.emailError) setError(created.emailError); else setError('');
      setShareSelection(null); recipientVersion.current++;
    } catch (e) { reportShareError(failure(e)); } finally { setSending(false); }
  }
  async function manageShares() {
    setBusy(true); setError('');
    try { const value = await adminQualityCycle.listReportShares(); if (!value) throw new Error('Could not load shared reports.'); setShares(value); }
    catch (e) { reportError(failure(e)); } finally { setBusy(false); }
  }
  async function retry(share: ReportShare) {
    setBusy(true); setError('');
    try { const updated = await adminQualityCycle.retryEmail(share.id); setShares(current => current.map(s => s.id === updated.id ? updated : s)); if (updated.emailError) setError(updated.emailError); }
    catch (e) { reportError(failure(e)); } finally { setBusy(false); }
  }
  return <Stack>
    <Paper withBorder p="md"><Stack>
      <Title order={3}>Quality reports</Title>
      <MultiSelect label="Shifts" placeholder="Select shifts" searchable clearable maxValues={50} data={shifts.map(s => ({ value: s.id, label: `${s.name}${s.isActive ? '' : ' (inactive)'}` }))} value={shiftIds} onChange={setShiftIds} disabled={busy || sending} />
      <SegmentedControl aria-label="Report period type" value={mode} onChange={setMode} disabled={busy || sending} data={[{ value: 'months', label: 'Months' }, { value: 'range', label: 'Date range' }]} />
      {mode === 'months' ? <Group align="flex-end">
        <NumberInput label="Month selection year" min={2000} max={2100} value={year} onChange={setYear} disabled={busy || sending} w={160} />
        <MultiSelect label="Months" searchable clearable maxValues={12} data={monthOptions} value={months} onChange={setMonths} disabled={busy || sending} style={{ flex: 1, minWidth: 220 }} />
      </Group> : <Group grow>
        <TextInput label="From date" type="date" value={from} onChange={e => setFrom(e.target.value)} disabled={busy || sending} />
        <TextInput label="To date (inclusive)" type="date" value={to} min={from} onChange={e => setTo(e.target.value)} disabled={busy || sending} />
      </Group>}
      <Group justify="space-between" align="flex-end">
        <Button onClick={load} loading={busy} disabled={!valid || sending}>Load report</Button>
        <Select label="Report language" allowDeselect={false} value={language} onChange={value => setLanguage(value as typeof language)} data={[{ value: 'en', label: 'English' }, { value: 'fi', label: 'Finnish' }, { value: 'sv', label: 'Swedish' }]} disabled={busy || sending} />
      </Group>
      {error && <Text c="red" role="alert">{error}</Text>}
    </Stack></Paper>
    {report && <Paper withBorder p="md"><Stack>
      <Text size="sm">{report.periods?.map(p => `${dateLabel(p.from)} – ${dateLabel(p.to)}`).join('; ')} · {report.timeZoneId}</Text>
      <Text size="sm" c="dimmed">Scheduled hours count each visit once. Actual hours sum each cleaner’s submitted work time; incomplete timings are excluded.</Text>
      <Group><Button disabled={busy || !report.shifts.length} onClick={() => download()}>Export all selected shifts</Button><Button variant="light" disabled={busy || !report.shifts.length} onClick={() => openShare()}>Share all selected shifts</Button></Group>
      <DsGrid<(typeof report.shifts)[number]>
        label="Quality report shifts"
        rows={report.shifts}
        getRowId={(row) => `${row.shiftId}-${row.branchId}`}
        columns={[
          { headerName: 'Company', colId: 'company', valueGetter: p => p.data?.companyName ?? '' },
          { headerName: 'Branch', colId: 'branch', valueGetter: p => p.data?.branchName ?? '' },
          { headerName: 'Shift', colId: 'shift', flex: 2, valueGetter: p => p.data?.shiftName ?? '' },
          { headerName: 'Scheduled occurrences', colId: 'scheduled', valueGetter: p => p.data?.scheduledOccurrences ?? '' },
          { headerName: 'Completed occurrences', colId: 'completed', valueGetter: p => p.data?.completedOccurrences ?? '' },
          { headerName: 'Scheduled hours', colId: 'scheduledHours', valueGetter: p => p.data ? p.data.scheduledHours.toFixed(2) : '' },
          { headerName: 'Actual hours', colId: 'actualHours', cellRenderer: (p: { data?: (typeof report.shifts)[number] }) => p.data ? (
            <div>{p.data.recordedStaffHours == null ? 'Not available' : p.data.recordedStaffHours.toFixed(2)}{(p.data.incompleteTimingRecords ?? 0) > 0 && <div className="text-xs text-gray-600">{p.data.incompleteTimingRecords} incomplete</div>}</div>
          ) : null },
          { headerName: 'Actions', colId: 'actions', sortable: false, minWidth: 260, flex: 0, width: 280, cellRenderer: (p: { data?: (typeof report.shifts)[number] }) => p.data ? (
            <div className="flex items-center gap-2"><Button size="xs" disabled={busy} onClick={() => download(p.data!.shiftId)}>Export shift</Button><Button size="xs" variant="light" disabled={busy} onClick={() => openShare(p.data!.shiftId)}>Share shift</Button></div>
          ) : null },
        ]}
      />
      {!report.shifts.length && <Text>No reportable visits for these shifts and dates.</Text>}
    </Stack></Paper>}
    <Paper withBorder p="md"><Stack>
      <Group justify="space-between"><Title order={3}>Shared reports</Title><Button variant="light" onClick={manageShares} disabled={busy}>Refresh shared reports</Button></Group>
      <Text size="sm" c="dimmed">Links expire after 30 days and include report photos and notes. Acknowledgment records receipt through the link, not a signature.</Text>
      {shares.map(share => {
        const inactive = share.isRevoked || Date.parse(share.expiresUtc) <= Date.now();
        return <Paper key={share.id} withBorder p="sm"><Stack gap="xs">
          <Group><Text fw={600}>{share.recipientEmail ?? 'Existing report link'}</Text><Badge color={share.acknowledgedUtc ? 'green' : share.emailError ? 'red' : 'gray'}>{share.acknowledgedUtc ? 'Acknowledged' : share.emailError ? 'Email failed' : share.emailSentUtc ? 'Email sent' : 'Link created'}</Badge>{inactive && <Badge color="gray">Inactive</Badge>}</Group>
          <Text size="sm">{share.periods?.map(p => `${dateLabel(p.from)} – ${dateLabel(p.to)}`).join('; ') ?? `${share.year}-${String(share.month).padStart(2, '0')}`} · {(share.language ?? 'en').toUpperCase()} · Expires {new Date(share.expiresUtc).toLocaleDateString()}</Text>
          {share.emailSentUtc && <Text size="xs">Accepted by email provider: {new Date(share.emailSentUtc).toLocaleString()}</Text>}
          {share.acknowledgedUtc && <Text size="xs">Acknowledged: {new Date(share.acknowledgedUtc).toLocaleString()}</Text>}
          {share.emailError && <Text size="sm" c="red">{share.emailError}</Text>}
          {!inactive && <Group><Button component="a" size="xs" href={share.recipientEmail ? reportLandingUrl(share) : reportShareUrl(share)} target="_blank" rel="noreferrer">Open report</Button>
            {share.recipientEmail && !share.emailSentUtc && <Button size="xs" disabled={busy} onClick={() => retry(share)}>Retry email</Button>}
            <Button size="xs" color="red" variant="light" disabled={busy} onClick={async () => { setBusy(true); try { const result = await adminQualityCycle.revokeReportShare(share.id); if (!result.ok) throw new Error(result.message); setShares(current => current.map(s => s.id === share.id ? { ...s, isRevoked: true } : s)); } catch (e) { reportError(failure(e)); } finally { setBusy(false); } }}>Revoke</Button>
          </Group>}
        </Stack></Paper>;
      })}
    </Stack></Paper>
    <Modal styles={{ content: { color: 'var(--mantine-color-text)' } }} opened={!!shareSelection} onClose={() => { if (!sending) { setShareSelection(null); recipientVersion.current++; } }} title="Share quality report" closeOnClickOutside={!sending} closeOnEscape={!sending} withCloseButton={!sending}>
      <form onSubmit={e => { e.preventDefault(); void send(); }}><Stack>
        <Text size="sm">Send {shareSelection?.shiftIds.length} selected shift(s) as one report. The link expires in 30 days.</Text>
        <Select label="Recipient source" allowDeselect={false} value={recipientChoice} disabled={sending} data={[...recipients.map(r => ({ value: r.email, label: `${r.label} — ${r.email}` })), { value: 'other', label: 'Other email address' }]} onChange={value => { setRecipientChoice(value ?? 'other'); setEmail(value === 'other' ? '' : value ?? ''); }} />
        <TextInput label="Recipient email" type="email" required maxLength={254} value={email} readOnly={recipientChoice !== 'other'} disabled={sending} onChange={e => setEmail(e.target.value)} />
        <Select label="PDF and email language" value={shareSelection?.language ?? 'en'} allowDeselect={false} disabled={sending} data={[{ value: 'en', label: 'English' }, { value: 'fi', label: 'Finnish' }, { value: 'sv', label: 'Swedish' }]} onChange={value => setShareSelection(current => current ? { ...current, language: value as ReportSelection['language'] } : null)} />
        {shareError && <Text role="alert" c="red">{shareError}</Text>}
        <Button type="submit" loading={sending}>Send report</Button>
      </Stack></form>
    </Modal>
  </Stack>;
}
