'use client';

import { useState } from 'react';
import { Alert, Button, Group, Modal, MultiSelect, Select, Stack, Text, Textarea } from '@mantine/core';
import { adminShifts, type Employee, type ShiftOccurrence, type VisitAssignmentResult } from '@/lib/adminApi';

export function VisitAssigneesDialog({ shiftId, visit, employees, timeZone, onClose, onSaved }: {
  shiftId: string; visit: ShiftOccurrence; employees: Employee[]; timeZone: string;
  onClose: () => void; onSaved: () => Promise<void>;
}) {
  const [scope, setScope] = useState('visit');
  const [mode, setMode] = useState('1');
  const [selected, setSelected] = useState<string[]>(visit.overrideEmployeeIds ?? visit.assigneeIds ?? []);
  const [reason, setReason] = useState('');
  const [preview, setPreview] = useState<VisitAssignmentResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const name = (id: string) => { const employee = employees.find(x => x.id === id); return employee ? `${employee.firstName} ${employee.lastName}` : 'Former employee'; };
  const changed = () => { setPreview(null); setError(''); };
  const submit = async (isPreview: boolean) => {
    if (busy) return;
    if (!reason.trim() || (mode !== '0' && selected.length === 0)) { setError('Select employees and provide a reason.'); return; }
    setBusy(true); setError('');
    try {
      const result = await adminShifts.setVisitAssignees(shiftId, {
        originalStartUtc: visit.status === 4 ? null : visit.originalStartUtc ?? visit.startUtc,
        extraExceptionId: visit.status === 4 ? visit.exceptionId ?? null : null,
        mode: Number(mode), employeeIds: mode === '0' ? [] : selected, reason: reason.trim(),
        rosterVersion: visit.rosterVersion ?? '', assignmentVersion: visit.assignmentVersion ?? null,
        fromThisDate: scope === 'future', preview: isPreview,
      });
      if (isPreview) setPreview(result);
      else { await onSaved(); onClose(); }
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not change assignments. Retry or reload occurrences.'); }
    finally { setBusy(false); }
  };
  return <Modal opened onClose={() => { if (!busy) onClose(); }} title="Assign visit" closeOnClickOutside={!busy} closeOnEscape={!busy}>
    <Stack>
      <Text>{new Date(visit.startUtc).toLocaleString(undefined, { timeZone })} ({timeZone})</Text>
      <Select label="Apply to" value={scope} disabled={busy} onChange={value => { setScope(value ?? 'visit'); if (value === 'future' && mode === '0') setMode('1'); changed(); }}
        data={[{ value: 'visit', label: 'This occurrence only' }, { value: 'future', label: 'From this date onward' }]} />
      <Select label="Assignment action" value={mode} disabled={busy} onChange={value => { setMode(value ?? '1'); changed(); }}
        data={[{ value: '1', label: 'Replace regular assignees' }, { value: '2', label: 'Add extra employees' }, ...(scope === 'visit' ? [{ value: '0', label: 'Restore recurring assignment' }] : [])]} />
      {mode !== '0' && <MultiSelect label="Employees" searchable required disabled={busy}
        data={employees.filter(x => x.isActive).map(x => ({ value: x.id, label: name(x.id) }))}
        value={selected} onChange={value => { setSelected(value); changed(); }} />}
      <Textarea label="Assignment reason" required maxLength={500} disabled={busy} value={reason} onChange={event => { setReason(event.currentTarget.value); changed(); }} />
      {scope === 'future' && <Text size="sm">Starts at midnight on this date in the tenant timezone. Replace also removes later recurring handovers. Individual visit overrides remain in place.</Text>}
      {preview && <Alert title="Assignment preview"><Text>Selected visit: {preview.employeeIds.map(name).join(', ') || 'Unassigned'}</Text><Text size="sm">{preview.message}</Text></Alert>}
      {error && <Text role="alert" c="red">{error}</Text>}
      <Group justify="flex-end"><Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button>
        <Button variant="light" loading={busy} onClick={() => submit(true)}>Preview assignment</Button>
        <Button disabled={!preview || busy} onClick={() => submit(false)}>Save assignment</Button></Group>
    </Stack>
  </Modal>;
}
