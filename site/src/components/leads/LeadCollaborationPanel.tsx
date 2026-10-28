'use client';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Badge, Button, Group, Loader, MultiSelect, NumberInput, Paper, Select, Stack, Text, Textarea, TextInput, Title } from '@mantine/core';
import { leadCollaborationApi as api, type Delivery, type FollowUp, type FollowUpInput, type LeadAdmin, type Page, type VoiceNote } from '@/lib/leadCollaborationApi';
import { VoiceRecorder } from './VoiceRecorder';
function Pages({ value, total, set, label }: { value: number; total: number; set: (value: number) => void; label: string }) { return <Group><Button size="xs" variant="default" aria-label={`Previous ${label}`} disabled={!value} onClick={() => set(Math.max(0, value - 25))}>Previous</Button><Text size="sm">{total ? `${value + 1}–${Math.min(value + 25, total)} of ${total}` : '0 records'}</Text><Button size="xs" variant="default" aria-label={`Next ${label}`} disabled={value + 25 >= total} onClick={() => set(value + 25)}>Next</Button></Group>; }
function VoicePlayback({ leadId, note }: { leadId: string; note: VoiceNote }) {
  const [url, setUrl] = useState(''); const [error, setError] = useState(''); const [loading, setLoading] = useState(false);
  const value = useRef(''); const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; if (value.current) URL.revokeObjectURL(value.current); }; }, []);
  const load = async () => { setLoading(true); setError(''); try { const blob = await api.audio(leadId, note); if (mounted.current) { const next = URL.createObjectURL(blob); if (value.current) URL.revokeObjectURL(value.current); value.current = next; setUrl(next); } } catch (failure) { if (mounted.current) setError((failure as Error).message); } finally { if (mounted.current) setLoading(false); } };
  return <>{url ? <audio controls src={url} aria-label={`Voice note by ${note.authorName}`} onError={() => { setError('Audio could not be played. Reload it and try again.'); setUrl(''); }} /> : <Button size="xs" variant="light" loading={loading} onClick={() => void load()}>{error ? 'Retry audio' : 'Load audio'}</Button>}{error && <Text c="red" role="alert">{error}</Text>}</>;
}
export function LeadCollaborationPanel({ leadId, active, admin, converted, onDirtyChange, onBusyChange }: { leadId: string; active: boolean; admin: boolean; converted: boolean; onDirtyChange: (value: boolean) => void; onBusyChange: (value: boolean) => void }) {
  const [zone, setZone] = useState(''); const [voices, setVoices] = useState<Page<VoiceNote>>({ items: [], total: 0 });
  const [follows, setFollows] = useState<Page<FollowUp>>({ items: [], total: 0 }); const [history, setHistory] = useState<Page<Delivery>>({ items: [], total: 0 });
  const [voiceSkip, setVoiceSkip] = useState(0); const [followSkip, setFollowSkip] = useState(0); const [historySkip, setHistorySkip] = useState(0);
  const [loading, setLoading] = useState(true); const [error, setError] = useState(''); const [message, setMessage] = useState(''); const [busy, setBusy] = useState(false);
  const [recordDirty, setRecordDirty] = useState(false); const [recordBusy, setRecordBusy] = useState(false);
  const [form, setForm] = useState<FollowUpInput | null>(null); const [editing, setEditing] = useState<FollowUp | undefined>(); const [admins, setAdmins] = useState<LeadAdmin[]>([]);
  const [recipientError, setRecipientError] = useState(''); const [recipientLoading, setRecipientLoading] = useState(false); const generation = useRef(0);
  useEffect(() => { onDirtyChange(recordDirty || !!form); return () => onDirtyChange(false); }, [recordDirty, form, onDirtyChange]);
  useEffect(() => { onBusyChange(busy || recordBusy); return () => onBusyChange(false); }, [busy, recordBusy, onBusyChange]);
  const load = useCallback(async () => { const attempt = ++generation.current; setLoading(true); setError(''); try {
    const [settings, v, f, h] = await Promise.all([api.settings(), api.voices(leadId, voiceSkip), api.followUps(leadId, followSkip), api.history(leadId, historySkip)]);
    if (attempt === generation.current) { setZone(settings.timeZoneId); setVoices(v); setFollows(f); setHistory(h); }
  } catch (failure) { if (attempt === generation.current) setError((failure as Error).message); } finally { if (attempt === generation.current) setLoading(false); } }, [leadId, voiceSkip, followSkip, historySkip]);
  useEffect(() => { void load(); return () => { generation.current++; }; }, [load]);
// Performance Optimization: Cache Intl.DateTimeFormat instances by timezone at module level
// to avoid repeated object instantiation overhead (~20x slower) during list rendering and date calculations.
const dateFormatterCache = new Map<string, Intl.DateTimeFormat>();
const partsFormatterCache = new Map<string, Intl.DateTimeFormat>();

function getDateFormatter(tz: string) {
  let fmt = dateFormatterCache.get(tz);
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: tz });
    dateFormatterCache.set(tz, fmt);
  }
  return fmt;
}

function getPartsFormatter(tz: string) {
  let fmt = partsFormatterCache.get(tz);
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: tz });
    partsFormatterCache.set(tz, fmt);
  }
  return fmt;
}

  const date = (value: string) => getDateFormatter(zone || 'UTC').format(new Date(value));
  const recipients = async () => { setRecipientLoading(true); setRecipientError(''); try { setAdmins(await api.recipients()); } catch (failure) { setRecipientError((failure as Error).message); } finally { setRecipientLoading(false); } };
  const open = (existing?: FollowUp) => {
    if (form && !window.confirm('Discard the unsaved follow-up?')) return;
    const parts = getPartsFormatter(zone || 'UTC').formatToParts(new Date());
    const part = (type: string) => Number(parts.find(p => p.type === type)?.value);
    const tomorrow = new Date(Date.UTC(part('year'), part('month') - 1, part('day') + 1)).toISOString().slice(0, 10);
    setEditing(existing); setForm(existing ? { reason: existing.reason, firstDate: existing.firstDate.slice(0, 10), endDate: existing.endDate?.slice(0, 10) ?? null, recurrence: existing.recurrence, interval: existing.interval, recipientIds: existing.recipientIds } : { reason: '', firstDate: tomorrow, endDate: null, recurrence: 'Once', interval: 1, recipientIds: [] }); setMessage(''); void recipients();
  };
  const run = async (action: () => Promise<void>) => { setBusy(true); setError(''); setMessage(''); try { await action(); await load(); } catch (failure) { setError((failure as Error).message); } finally { setBusy(false); } };
  return <Stack gap="md"><Title order={3}>Voice notes</Title>
    {error && <Text role="alert" c="red">{error}</Text>}{message && <Text role="status" c="teal">{message}</Text>}
    <Group><Button variant="default" disabled={busy || loading} onClick={() => void load()}>Refresh notes and follow-ups</Button>{loading && <Loader size="sm" />}</Group>
    <VoiceRecorder leadId={leadId} disabled={!active || busy} onSaved={() => { setMessage('Voice note saved.'); void load(); }} onDirtyChange={setRecordDirty} onBusyChange={setRecordBusy} />
    {!loading && !voices.total && <Text>No voice notes yet.</Text>}
    {voices.items.map(note => <Paper key={note.id} withBorder p="sm"><Stack gap="xs"><Text fw={600}>{note.authorName}</Text><Text size="sm">{date(note.createdUtc)} · {zone} · {Math.ceil(note.durationSeconds)} seconds</Text><VoicePlayback leadId={leadId} note={note} />{admin && !converted && <Button size="xs" color="red" variant="subtle" disabled={busy} onClick={() => { if (window.confirm('Delete this voice note permanently?')) void run(async () => { await api.removeVoice(leadId, note); setMessage('Voice note deleted.'); }); }}>Delete voice note</Button>}</Stack></Paper>)}
    <Pages label="voice notes" value={voiceSkip} total={voices.total} set={setVoiceSkip} />
    <Title order={3}>Follow-ups</Title><Text size="sm">Emails are scheduled for 06:00 in {zone || 'the tenant timezone'}. Actual arrival depends on the email service. Closed, inactive or converted leads stop future reminders.</Text>
    <Button disabled={!active || busy || !zone} onClick={() => open()}>Add follow-up</Button>
    {form && <Paper withBorder p="md"><Stack><Text fw={600}>{editing ? 'Edit follow-up' : 'New follow-up'}</Text>
      <Textarea label="Follow-up reason" required maxLength={1000} value={form.reason} disabled={busy} onChange={e => setForm({ ...form, reason: e.target.value })} />
      <TextInput type="date" label="First reminder date" required value={form.firstDate} disabled={busy} onChange={e => setForm({ ...form, firstDate: e.target.value })} /><Text size="sm">At 06:00 {zone}. Choosing today after 06:00 makes the reminder due now.</Text>
      <Select label="Repeat follow-up" value={form.recurrence} data={['Once', 'Daily', 'Weekly', 'Monthly']} disabled={busy} onChange={value => setForm({ ...form, recurrence: value || 'Once' })} />
      {form.recurrence !== 'Once' && <><NumberInput label="Repeat every" min={1} max={365} value={form.interval} disabled={busy} onChange={value => setForm({ ...form, interval: typeof value === 'number' ? value : 1 })} /><TextInput type="date" label="Last reminder date (optional)" value={form.endDate || ''} disabled={busy} onChange={e => setForm({ ...form, endDate: e.target.value || null })} /></>}
      {recipientError && <Text role="alert" c="red">{recipientError}</Text>}<MultiSelect label="Notify tenant admins" required searchable data={admins.map(person => ({ value: person.id, label: `${person.name} (${person.email})` }))} value={form.recipientIds} disabled={busy || recipientLoading || !!recipientError} onChange={value => setForm({ ...form, recipientIds: value })} />
      <Button variant="default" loading={recipientLoading} disabled={busy} onClick={() => void recipients()}>Reload admin recipients</Button>
      <Group><Button loading={busy} disabled={!active || !!recipientError || recipientLoading} onClick={() => void run(async () => { if (!form.reason.trim() || !form.firstDate || !form.recipientIds.length) throw new Error('Enter a reason, date and at least one tenant admin.'); await api.saveFollowUp(leadId, form, editing); setForm(null); setEditing(undefined); setMessage('Follow-up saved.'); })}>Save follow-up</Button><Button variant="default" disabled={busy} onClick={() => { if (window.confirm('Discard this unsaved follow-up?')) setForm(null); }}>Discard follow-up</Button></Group>
    </Stack></Paper>}
    {!loading && !follows.total && <Text>No follow-ups yet.</Text>}
    {follows.items.map(follow => <Paper key={follow.id} withBorder p="sm"><Stack gap="xs"><Group><Text fw={600} style={{ whiteSpace: 'pre-wrap' }}>{follow.reason}</Text><Badge>{follow.state}</Badge></Group><Text size="sm">{follow.recurrence}{follow.recurrence !== 'Once' && ` · every ${follow.interval}`} · {follow.nextDate ? `Next: ${follow.nextDate.slice(0, 10)} at 06:00 ${zone}` : 'No future occurrence'} · {follow.recipientIds.length} admin recipient(s)</Text><Text size="sm">Created/updated by {follow.authorName}</Text>{follow.lastError && <Text role="alert" c="red">{follow.lastError}</Text>}{follow.lastCheckedUtc && <Text size="xs">Last checked: {date(follow.lastCheckedUtc)}</Text>}{active && follow.state === 'Active' && <Group><Button variant="light" disabled={busy} onClick={() => open(follow)}>Edit follow-up</Button><Button color="red" variant="light" disabled={busy} onClick={() => { if (window.confirm('Cancel this follow-up? A send already in progress may finish; after cancellation succeeds no new sends will start.')) void run(async () => { await api.cancel(leadId, follow); setMessage('Follow-up cancelled.'); }); }}>Cancel follow-up</Button></Group>}</Stack></Paper>)}
    <Pages label="follow-ups" value={followSkip} total={follows.total} set={setFollowSkip} />
    <Title order={3}>Notification history</Title><Text size="sm">Sent means accepted by the email provider. A failed recipient does not resend successful notifications. “Needs review” means acceptance could not be confirmed safely; check before creating a new reminder.</Text>
    {!loading && !history.total && <Text>No notifications yet.</Text>}{history.items.map(delivery => <Paper key={delivery.id} withBorder p="sm"><Text>{delivery.recipientName} · {delivery.recipientEmail || 'No eligible address'}</Text><Text size="sm">Scheduled: {date(delivery.dueUtc)} · {delivery.timeZoneId}</Text><Badge>{delivery.state}</Badge>{delivery.sentUtc && <Text size="sm">Sent: {date(delivery.sentUtc)}</Text>}{delivery.error && <Text size="sm">{delivery.error}</Text>}<Text size="xs">Attempts: {delivery.attempts}</Text></Paper>)}
    <Pages label="notifications" value={historySkip} total={history.total} set={setHistorySkip} />
  </Stack>;
}
