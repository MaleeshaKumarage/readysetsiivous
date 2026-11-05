'use client';

import { useState } from 'react';
import { Alert, Button, Group, Modal, MultiSelect, Select, Stack, Text, Textarea } from '@mantine/core';
import { adminShifts, VisitConflictError, type Employee, type ShiftConflictDetail, type ShiftOccurrence, type VisitAssignmentResult } from '@/lib/adminApi';

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
  const [conflicts, setConflicts] = useState<ShiftConflictDetail[]>([]);
  const name = (id: string) => { const employee = employees.find(x => x.id === id); return employee ? `${employee.firstName} ${employee.lastName}` : 'Former employee'; };
  const changed = () => { setPreview(null); setError(''); setConflicts([]); };
  const formatTime = (value: string) => new Date(value).toLocaleString(undefined, { timeZone });
  const formatEnd = (value: string) => new Date(value).toLocaleTimeString(undefined, { timeZone, hour: '2-digit', minute: '2-digit' });
  // Removes the overlapping cleaner from the clashing visit only, then re-previews this assignment.
  const removeClash = async (conflict: ShiftConflictDetail) => {
    if (busy) return;
    setBusy(true); setError('');
    try {
      const from = new Date(new Date(conflict.startUtc).getTime() - 60_000).toISOString();
      const to = new Date(new Date(conflict.endUtc).getTime() + 60_000).toISOString();
      const occurrences = (await adminShifts.occurrences(conflict.shiftId, from, to)) ?? [];
      const clash = occurrences.find(o => o.startUtc === conflict.startUtc)
        ?? occurrences.find(o => o.startUtc < conflict.endUtc && o.endUtc > conflict.startUtc);
      if (!clash) throw new Error('That overlapping visit is no longer scheduled. Reload occurrences and preview again.');
      const clashCurrent = clash.overrideEmployeeIds ?? clash.assigneeIds ?? [];
      const clashing = clashCurrent.filter(id => selected.includes(id));
      if (clashing.length === 0) throw new Error('The selected cleaners are no longer on the overlapping visit. Preview again to continue.');
      await adminShifts.setVisitAssignees(conflict.shiftId, {
        originalStartUtc: clash.status === 4 ? null : clash.originalStartUtc ?? clash.startUtc,
        extraExceptionId: clash.status === 4 ? clash.exceptionId ?? null : null,
        mode: 1, employeeIds: clashCurrent.filter(id => !clashing.includes(id)), reason: reason.trim(),
        rosterVersion: clash.rosterVersion ?? '', assignmentVersion: clash.assignmentVersion ?? null,
        fromThisDate: false, preview: false,
      });
      setConflicts([]);
      setBusy(false);
      await submit(true);
      return;
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not remove the overlapping cleaner. Reload occurrences and retry.'); }
    finally { setBusy(false); }
  };
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
    } catch (failure) {
      if (failure instanceof VisitConflictError) setConflicts(failure.conflicts);
      setError(failure instanceof Error ? failure.message : 'Could not change assignments. Retry or reload occurrences.');
    }
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
        data={employees.filter(x => x.isActive && x.role !== 'partner').map(x => ({ value: x.id, label: name(x.id) }))}
        value={selected} onChange={value => { setSelected(value); changed(); }} />}
      <Textarea label="Assignment reason" required maxLength={500} disabled={busy} value={reason} onChange={event => { setReason(event.currentTarget.value); changed(); }} />
      {scope === 'future' && <Text size="sm">Starts at midnight on this date in the tenant timezone. Replace also removes later recurring handovers. Individual visit overrides remain in place.</Text>}
      {preview && <Alert title="Assignment preview"><Text>Selected visit: {preview.employeeIds.map(name).join(', ') || 'Unassigned'}</Text><Text size="sm">{preview.message}</Text></Alert>}
      {error && <Text role="alert" c="red">{error}</Text>}
      {conflicts.map(conflict => <Alert key={`${conflict.shiftId}-${conflict.startUtc}`} color="orange" title={`Overlaps ${conflict.shiftName}`}>
        <Text size="sm">{formatTime(conflict.startUtc)} to {formatEnd(conflict.endUtc)}. Remove the overlapping cleaner from that visit, then assign them here.</Text>
        <Button mt="sm" size="xs" color="orange" disabled={busy} onClick={() => removeClash(conflict)}>Remove from {conflict.shiftName} and assign here</Button>
      </Alert>)}
      <Group justify="flex-end"><Button variant="default" disabled={busy} onClick={onClose}>Cancel</Button>
        <Button variant="light" loading={busy} onClick={() => submit(true)}>Preview assignment</Button>
        <Button disabled={!preview || busy} onClick={() => submit(false)}>Save assignment</Button></Group>
    </Stack>
  </Modal>;
}
