'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Badge, Button, Checkbox, Group, Loader, Modal, Paper, Stack, Text, Textarea, TextInput, Title } from '@mantine/core';
import { calendarRequest, calendarLocalInput, calendarDuration, CalendarAccessError, type AdminShiftCalendar, type CalendarVisit, type CalendarEmployee } from '@/lib/adminShiftCalendarApi';
import { readShiftPhotos, photoNoteMessage, type ShiftPhoto } from '@/lib/shiftPhotos';

function addDay(day: string, amount: number) {
  const date = new Date(`${day}T12:00:00Z`); date.setUTCDate(date.getUTCDate() + amount); return date.toISOString().slice(0, 10);
}

export default function ShiftCalendarPage() {
  const [day, setDay] = useState('');
  const [data, setData] = useState<AdminShiftCalendar | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(''); const [denied, setDenied] = useState(false);
  const [refresh, setRefresh] = useState(0); const sequence = useRef(0);
  const [reviewOnly, setReviewOnly] = useState(false);
  const [editing, setEditing] = useState<{ visit: CalendarVisit; person: CalendarEmployee } | null>(null);
  const [start, setStart] = useState(''); const [end, setEnd] = useState(''); const [reason, setReason] = useState('');
  const [saveError, setSaveError] = useState(''); const [saving, setSaving] = useState(false); const [message, setMessage] = useState('');
  const errorSummary = useRef<HTMLDivElement>(null);
  useEffect(() => { if (saveError) errorSummary.current?.focus(); }, [saveError]);
  const load = useCallback(async () => {
    const id = ++sequence.current; setLoading(true); setError('');
    try {
      const result = await calendarRequest<AdminShiftCalendar>(day ? `?date=${encodeURIComponent(day)}` : '');
      if (id !== sequence.current) return;
      setData(result); setDenied(false);
      if (!day) setDay(result.date);
    } catch (failure) {
      if (id !== sequence.current) return;
      setError(failure instanceof Error ? failure.message : 'Could not load calendar.');
      if (failure instanceof CalendarAccessError) { setDenied(true); setData(null); }
    } finally { if (id === sequence.current) setLoading(false); }
  }, [day]);
  useEffect(() => { void load(); return () => { sequence.current++; }; }, [load, refresh]);
  // Refresh today's boundary and permissions when the page regains focus.
  useEffect(() => { const focus = () => setRefresh(value => value + 1); window.addEventListener('focus', focus); return () => window.removeEventListener('focus', focus); }, []);
  useEffect(() => {
    const interval = window.setInterval(() => setRefresh(value => value + 1), 60_000);
    return () => window.clearInterval(interval);
  }, []);
  // Photos an admin receives after the visit, attached to its work record.
  const [photoFor, setPhotoFor] = useState<{ visit: CalendarVisit; person: CalendarEmployee } | null>(null);
  const [photoItems, setPhotoItems] = useState<ShiftPhoto[]>([]);
  const [photoReason, setPhotoReason] = useState('');
  const [photoSaving, setPhotoSaving] = useState(false);
  const [photoError, setPhotoError] = useState('');
  const openPhotos = (visit: CalendarVisit, person: CalendarEmployee) => {
    setPhotoFor({ visit, person }); setPhotoItems([]); setPhotoReason(''); setPhotoError('');
  };
  const addPhotos = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    try {
      const urls = await readShiftPhotos(Array.from(files), photoItems);
      setPhotoItems(current => [...current, ...urls.map(url => ({ url, note: '' }))]); setPhotoError('');
    } catch (failure) { setPhotoError(failure instanceof Error ? failure.message : 'Could not read the photo.'); }
  };
  const savePhotos = async () => {
    if (!photoFor) return;
    if (photoItems.length === 0) { setPhotoError('Choose at least one photo.'); return; }
    if (photoItems.some(photo => !photo.note.trim())) { setPhotoError(photoNoteMessage); return; }
    if (!photoReason.trim()) { setPhotoError('Enter a reason.'); return; }
    setPhotoSaving(true); setPhotoError('');
    try {
      await calendarRequest('/work-photos', { method: 'POST', body: JSON.stringify({ shiftId: photoFor.visit.shiftId,
        employeeId: photoFor.person.employeeId, occurrenceStartUtc: photoFor.visit.startUtc, exceptionId: photoFor.visit.exceptionId,
        reason: photoReason, photos: photoItems.map(photo => ({ url: photo.url, note: photo.note.trim() })), version: photoFor.person.version }) });
      setPhotoFor(null); setMessage('Photos added to the visit.'); setRefresh(value => value + 1);
    } catch (failure) {
      setPhotoError(failure instanceof Error ? failure.message : 'Could not add the photos.');
      if (failure instanceof CalendarAccessError) { setDenied(true); setData(null); setPhotoFor(null); setError(failure.message); }
    } finally { setPhotoSaving(false); }
  };
  const open = (visit: CalendarVisit, person: CalendarEmployee) => {
    if (!data) return;
    setEditing({ visit, person }); setStart(calendarLocalInput(person.actualStartUtc ?? visit.startUtc, data.timeZoneId));
    setEnd(calendarLocalInput(person.actualEndUtc ?? visit.endUtc, data.timeZoneId)); setReason(''); setSaveError(''); setMessage('');
  };
  const save = async () => {
    if (!editing || !start || !end || !reason.trim()) { setSaveError('Enter both times and a reason.'); return; }
    if (end <= start) { setSaveError('End must be after start.'); return; }
    setSaving(true); setSaveError('');
    try {
      await calendarRequest('/work-time', { method: 'PUT', body: JSON.stringify({ shiftId: editing.visit.shiftId,
        employeeId: editing.person.employeeId, occurrenceStartUtc: editing.visit.startUtc, exceptionId: editing.visit.exceptionId,
        actualStartLocal: start, actualEndLocal: end, reason, version: editing.person.version }) });
      setEditing(null); setMessage('Work times saved.'); setRefresh(value => value + 1);
    } catch (failure) {
      setSaveError(failure instanceof Error ? failure.message : 'Could not save times.');
      if (failure instanceof CalendarAccessError) { setDenied(true); setData(null); setEditing(null); setError(failure.message); }
    } finally { setSaving(false); }
  };
  const formatTime = (utc: string) => new Intl.DateTimeFormat('en-GB', { timeZone: data?.timeZoneId, day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(utc));
  const duration = editing && data ? calendarDuration(start, end, data.timeZoneId) : null;
  const reviewCount = data?.occurrences.filter(visit => visit.needsReview).length ?? 0;
  const shownVisits = data?.occurrences.filter(visit => !reviewOnly || visit.needsReview) ?? [];
  return <Stack>
    <Title order={2} component="h1">Shift calendar</Title>
    {!denied && <Group align="end" wrap="wrap">
      <Button variant="default" disabled={!day || saving} onClick={() => setDay(addDay(day, -1))}>Previous day</Button>
      <TextInput label="Calendar date" type="date" min="1900-01-01" max="9998-12-31" value={day} disabled={saving} onChange={event => { if (event.target.value) setDay(event.target.value); }} />
      <Button variant="default" disabled={!day || saving} onClick={() => setDay(addDay(day, 1))}>Next day</Button>
      <Button variant="light" disabled={saving} onClick={() => { setDay(''); setRefresh(value => value + 1); }}>Today</Button>
      <Button variant="light" disabled={saving} onClick={() => setRefresh(value => value + 1)}>Refresh</Button>
    </Group>}
    {data && <Text size="sm" c="dimmed">Times shown in {data.timeZoneId}. Work times can be recorded for past dates only.</Text>}
    {data && <Group justify="space-between" wrap="wrap">
      <Text fw={500}>{reviewCount === 0 ? 'No visits need review on this day.' : `${reviewCount} ${reviewCount === 1 ? 'visit needs' : 'visits need'} review.`}</Text>
      <Checkbox label="Show only visits that need review" checked={reviewOnly} disabled={saving} onChange={event => setReviewOnly(event.currentTarget.checked)} />
    </Group>}
    {message && <Text role="status" c="green">{message}</Text>}
    {error && <Alert color="red" role="alert">{error}{!denied && <Button variant="subtle" onClick={() => setRefresh(value => value + 1)}>Retry</Button>}</Alert>}
    {loading ? <Loader aria-label="Loading shifts" /> : !error && data && (data.occurrences.length === 0 ? <Text>No shifts on this day.</Text> :
      shownVisits.length === 0 ? <Text>Turn off the review filter to see every visit.</Text> :
      [...shownVisits].sort((a, b) => a.startUtc.localeCompare(b.startUtc) || a.shiftName.localeCompare(b.shiftName)).map(visit => <Paper withBorder p="md" radius="md" key={`${visit.shiftId}:${visit.exceptionId ?? visit.startUtc}`}>
        <Stack gap="xs"><Group justify="space-between"><Title order={3}>{visit.shiftName}</Title>{visit.cancelled && <Badge color="gray">Cancelled</Badge>}</Group>
          <Text>{visit.companyName} · {visit.branchName}</Text><Text fw={600}>Scheduled: {formatTime(visit.startUtc)} – {formatTime(visit.endUtc)}</Text>
          {visit.ambiguous && <Text size="sm" c="dimmed">Overlapping visits require review before work times can be recorded.</Text>}
          {visit.assignees.length === 0 && <Badge color="orange">Unassigned</Badge>}
          {visit.assignees.map(person => <Group justify="space-between" key={person.employeeId} wrap="wrap">
            <Stack gap={2}><Text fw={500}>{person.name}</Text><Text size="sm">{person.status}{person.hours == null ? '' : ` · ${person.hours.toFixed(2)} hours`}</Text>
              {person.actualStartUtc && <Text size="sm">Actual: {formatTime(person.actualStartUtc)} – {person.actualEndUtc ? formatTime(person.actualEndUtc) : 'Not ended'}</Text>}
              {person.review && <Group gap={6}><Badge color="orange" variant="light">{person.review.kind === 'missing-completion' ? 'Missing completion' : 'Unusual time'}</Badge><Text size="sm" c="dimmed">{person.review.label}</Text></Group>}</Stack>
            <Group gap={6}>{person.canRecord && <Button variant="light" onClick={() => open(visit, person)}>{person.actualStartUtc ? 'Correct times' : 'Record work'}<span className="sr-only"> for {person.name}</span></Button>}
              {person.version && <Button variant="subtle" onClick={() => openPhotos(visit, person)}>Add photos<span className="sr-only"> for {person.name}</span></Button>}</Group>
          </Group>)}
        </Stack>
      </Paper>))}
    <Modal opened={!!photoFor} onClose={() => { if (!photoSaving) setPhotoFor(null); }} title="Add photos to this visit" closeOnClickOutside={!photoSaving} closeOnEscape={!photoSaving}>
      <Stack>
        <Text fw={600}>{photoFor?.person.name} · {photoFor?.visit.shiftName}</Text>
        <Text size="sm" c="dimmed">Use this for photos the cleaner sent you outside the app. Each photo needs its own note, and the reason is kept with the visit.</Text>
        {photoError && <Text role="alert" c="red">{photoError}</Text>}
        <input type="file" aria-label="Choose photos" accept="image/png,image/jpeg,image/webp,image/bmp" multiple disabled={photoSaving} onChange={event => { void addPhotos(event.currentTarget.files); event.currentTarget.value = ''; }} />
        {photoItems.map((photo, index) => <Stack gap={4} key={index}>
          <img src={photo.url} alt={`Photo ${index + 1}`} style={{ maxWidth: '100%', maxHeight: 180, objectFit: 'contain' }} />
          <TextInput label={`Note for photo ${index + 1}`} required value={photo.note} disabled={photoSaving}
            onChange={event => { const value = event.target.value; setPhotoItems(current => current.map((item, i) => i === index ? { ...item, note: value } : item)); }} />
        </Stack>)}
        <Textarea label="Reason" required autosize minRows={2} value={photoReason} disabled={photoSaving} onChange={event => setPhotoReason(event.target.value)} />
        <Group justify="flex-end"><Button variant="default" disabled={photoSaving} onClick={() => setPhotoFor(null)}>Cancel</Button><Button loading={photoSaving} onClick={savePhotos}>Save photos</Button></Group>
      </Stack>
    </Modal>
    <Modal opened={!!editing} onClose={() => { if (!saving) setEditing(null); }} title={editing?.person.actualStartUtc ? 'Correct work times' : 'Record work'} closeButtonProps={{ 'aria-label': 'Close work times' }} closeOnClickOutside={!saving} closeOnEscape={!saving}>
      <Stack><Text fw={600}>{editing?.person.name} · {editing?.visit.shiftName}</Text>
        {saveError && <div ref={errorSummary} role="alert" tabIndex={-1}><Text c="red">{saveError}</Text></div>}
        {editing && <Text size="sm">Scheduled: {formatTime(editing.visit.startUtc)} – {formatTime(editing.visit.endUtc)}</Text>}
        {editing?.person.review && <Text size="sm" c="dimmed">Review: {editing.person.review.label}. Times are not changed automatically; a reasoned correction clears this review.</Text>}
        <Text size="sm">Confirm actual times in {data?.timeZoneId}. Scheduled times are prefilled for new records. Saving records this past visit as completed. Checklist evidence is preserved.</Text>
        <TextInput label="Actual start" type="datetime-local" value={start} onChange={event => setStart(event.target.value)} required disabled={saving} />
        <TextInput label="Actual end" type="datetime-local" value={end} onChange={event => setEnd(event.target.value)} required disabled={saving} />
        {duration !== null && <Text size="sm">Work duration: {duration.toFixed(2)} hours</Text>}
        <Textarea label="Reason" value={reason} onChange={event => setReason(event.target.value)} required maxLength={2000} disabled={saving} />
        <Button onClick={save} loading={saving}>Save work times</Button>
      </Stack>
    </Modal>
  </Stack>;
}
