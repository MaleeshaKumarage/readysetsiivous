'use client';

import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Select, MultiSelect, Group, Stack, Text, Loader, ActionIcon, NumberInput,
} from '@mantine/core';
import { Plus, Trash2, Pencil } from 'lucide-react';
import {
  adminShifts, adminCompanies, adminEmployees, adminTenant,
  type Shift, type Company, type Branch, type Employee, type ShiftAssignment, type ShiftOccurrence,
} from '@/lib/adminApi';
import { ShiftPricingDialog } from '@/components/ShiftPricingDialog';
import { VisitAssigneesDialog } from '@/components/VisitAssigneesDialog';
import { isAdmin } from '@/lib/auth';
import { adminQualityCycle, type QualityCycleTemplate } from '@/lib/qualityCycleApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const SCHEDULE_TYPES = [
  { value: '0', label: 'Daily' },
  { value: '1', label: 'Varied times by weekday' },
  { value: '2', label: 'Specific days' },
  { value: '3', label: 'Bi-weekly' },
  { value: '5', label: 'Monthly' },
  { value: '4', label: 'On-call / flexible' },
];

const OCCURRENCE_STATUSES = [
  { value: '1', label: 'Move / change time' },
  { value: '2', label: 'Customer cancelled' },
  { value: '3', label: 'Company cancelled' },
  { value: '4', label: 'Extra visit' },
];

const localInputValue = (utc: string, timeZone: string) => {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
    .formatToParts(new Date(utc)).reduce<Record<string, string>>((all, part) => ({ ...all, [part.type]: part.value }), {});
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
};

const tenantLocalToUtc = (value: string, timeZone: string) => {
  const [date, time] = value.split('T');
  const [year, month, day] = date.split('-').map(Number); const [hour, minute] = time.split(':').map(Number);
  const wanted = Date.UTC(year, month - 1, day, hour, minute); let instant = wanted;
  for (let attempt = 0; attempt < 4; attempt++) {
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
      .formatToParts(new Date(instant)).reduce<Record<string, string>>((all, part) => ({ ...all, [part.type]: part.value }), {});
    const represented = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute));
    if (represented === wanted) return new Date(instant).toISOString();
    instant += wanted - represented;
  }
  throw new Error('That local time does not exist in the tenant time zone. Choose another time.');
};

const addCalendarDays = (date: string, days: number) => {
  const [year, month, day] = date.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
};

export default function ShiftsAdminPage() {
  const [pricingShift, setPricingShift] = useState<Shift | null>(null);
  const [pricingOccurrence, setPricingOccurrence] = useState<ShiftOccurrence | undefined>();
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [loading, setLoading] = useState(true);
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const [listError, setListError] = useState('');
  const [timeZoneId, setTimeZoneId] = useState('Europe/Helsinki');
  const [schedulingReady, setSchedulingReady] = useState(false);
  const [restoring, setRestoring] = useState<string | null>(null);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingShift, setEditingShift] = useState<Shift | null>(null);

  const [companyId, setCompanyId] = useState<string | null>(null);
  const [branchId, setBranchId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [validFrom, setValidFrom] = useState('');
  const [validUntil, setValidUntil] = useState('');
  const [scheduleType, setScheduleType] = useState('0');
  const [start, setStart] = useState('08:00');
  const [end, setEnd] = useState('17:00');
  const [weekDays, setWeekDays] = useState<string[]>([]);
  const [weekParity, setWeekParity] = useState('1');
  const [monthlyDay, setMonthlyDay] = useState<string | number>(1);
  const [dailyTimes, setDailyTimes] = useState<Record<string, { start: string; end: string }>>({});
  const [notes, setNotes] = useState('');
  const [qualityCycleTemplateId, setQualityCycleTemplateId] = useState<string | null>(null);
  const [templates, setTemplates] = useState<QualityCycleTemplate[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const [templateShift, setTemplateShift] = useState<Shift | null>(null);
  const [assignedTemplate, setAssignedTemplate] = useState<string | null>(null);
  const [templateError, setTemplateError] = useState('');
  const [templateSaving, setTemplateSaving] = useState(false);

  const [assignOpen, setAssignOpen] = useState<string | null>(null);
  const [assignments, setAssignments] = useState<Record<string, ShiftAssignment[]>>({});
  const [assignSel, setAssignSel] = useState<string[]>([]);
  const [assignLoading, setAssignLoading] = useState(false);
  const [assignBusy, setAssignBusy] = useState(false);
  const [assignError, setAssignError] = useState('');
  const [assignStatus, setAssignStatus] = useState('');
  const assignmentGeneration = useRef(0);
  const assignmentPending = useRef(false);
  const [occurrenceShift, setOccurrenceShift] = useState<Shift | null>(null);
  const [occurrences, setOccurrences] = useState<ShiftOccurrence[]>([]);
  const [occurrenceLoading, setOccurrenceLoading] = useState(false);
  const [occurrenceError, setOccurrenceError] = useState('');
  const [occurrenceEditError, setOccurrenceEditError] = useState('');
  const [occurrenceFrom, setOccurrenceFrom] = useState(new Date().toISOString().slice(0, 10));
  const [occurrenceTo, setOccurrenceTo] = useState(new Date(Date.now() + 31 * 86400000).toISOString().slice(0, 10));
  const [editingOccurrence, setEditingOccurrence] = useState<ShiftOccurrence | null>(null);
  const [occurrenceStatus, setOccurrenceStatus] = useState('1');
  const [occurrenceStart, setOccurrenceStart] = useState('');
  const [occurrenceEnd, setOccurrenceEnd] = useState('');
  const [occurrenceReason, setOccurrenceReason] = useState('');
  const [occurrenceEmployee, setOccurrenceEmployee] = useState<string | null>(null);
  const [occurrenceSaving, setOccurrenceSaving] = useState(false);
  const [assigningVisit, setAssigningVisit] = useState<ShiftOccurrence | null>(null);
  useEffect(() => () => { assignmentGeneration.current++; }, []);

  const load = () => adminShifts.list(undefined, undefined, true).then((r) => { if (r) setShifts(r); setLoading(false); });
  useEffect(() => { load(); adminTenant.get().then(profile => {
    if (!profile) throw new Error('Could not load scheduling settings. Refresh the page to retry.');
    const zone = profile.timeZoneId?.trim() || 'Europe/Helsinki';
    new Intl.DateTimeFormat('en-CA', { timeZone: zone }).format(new Date());
    setTimeZoneId(zone); setSchedulingReady(true);
  }).catch(() => setListError('Could not load scheduling time zone. Refresh the page or ask a tenant administrator to check its settings.')); }, []);

  const closeAssignments = () => {
    assignmentGeneration.current++;
    setAssignOpen(null); setAssignSel([]); setAssignLoading(false);
    setAssignError(''); setAssignStatus('');
  };
  const changeLifecycle = (status: LifecycleStatus) => {
    if (status === lifecycle) return;
    closeAssignments(); setTemplateShift(null); setLifecycle(status);
  };

  // Performance Optimization: Memoize visible shifts and counts to avoid recalculating
  // filtered arrays on every component render (e.g., input updates in modals or selection state changes).
  const visibleShifts = useMemo(
    () => shifts.filter((s) => inLifecycle(s, lifecycle)),
    [shifts, lifecycle]
  );

  const { activeCount, inactiveCount } = useMemo(() => {
    if (loading) return { activeCount: undefined, inactiveCount: undefined };
    let active = 0;
    let inactive = 0;
    for (const s of shifts) {
      if (s.isActive) active++;
      else inactive++;
    }
    return { activeCount: active, inactiveCount: inactive };
  }, [loading, shifts]);

  useEffect(() => {
    adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); });
    adminEmployees.list().then((e) => { if (e) setEmployees(e); });
    adminQualityCycle.listTemplates().then((t) => { if (t) setTemplates(t.filter((x) => x.isActive)); });
  }, []);

  useEffect(() => {
    if (!companyId) { setBranches([]); return; }
    adminCompanies.get(companyId).then((d) => { if (d) setBranches(d.branches); });
  }, [companyId]);

  const buildSchedule = () => {
    const t = Number(scheduleType);
    if (t === 0) return { type: 0, dailyStart: start, dailyEnd: end };
    if (t === 1) return { type: 1, dailyTimes: Object.fromEntries(weekDays.map(day => [DAYS[Number(day)], dailyTimes[day] ?? { start, end }])) };
    if (t === 2) return {
      type: 2,
      weeklyDay: weekDays.length ? Number(weekDays[0]) : null,
      weeklyDays: weekDays.map((d) => Number(d)),
      weeklyStart: start,
      weeklyEnd: end,
    };
    if (t === 3) return {
      type: 3,
      biWeeklyWeekParity: Number(weekParity),
      biWeeklyDay: weekDays.length ? Number(weekDays[0]) : 0,
      biWeeklyStart: start,
      biWeeklyEnd: end,
    };
    if (t === 5) return { type: 5, monthlyDay: Number(monthlyDay), monthlyStart: start, monthlyEnd: end };
    return { type: 4 };
  };

  const closeModal = () => {
    setName(''); setNotes(''); setWeekDays([]); setCompanyId(null); setBranchId(null); setQualityCycleTemplateId(null); setScheduleType('0'); setStart('08:00'); setEnd('17:00'); setWeekParity('1'); setMonthlyDay(1); setDailyTimes({}); setEditingShift(null); setError(''); setModalOpen(false);
  };

  const openCreate = () => { if (!schedulingReady) return; closeModal(); setValidFrom(localInputValue(new Date().toISOString(), timeZoneId).slice(0, 10)); setValidUntil(''); setModalOpen(true); };
  const openEdit = async (shift: Shift) => {
    const schedule = shift.schedule;
    setEditingShift(shift); setCompanyId(shift.companyId); setBranchId(shift.branchId);
    setName(shift.name); setNotes(shift.notes ?? ''); setQualityCycleTemplateId(shift.qualityCycleTemplateId ?? null);
    setValidFrom(shift.validFrom?.slice(0, 10) ?? ''); setValidUntil(shift.validUntil?.slice(0, 10) ?? '');
    setScheduleType(String(schedule.type));
    setStart((schedule.dailyStart ?? schedule.weeklyStart ?? schedule.biWeeklyStart ?? schedule.monthlyStart ?? '08:00').slice(0, 5));
    setEnd((schedule.dailyEnd ?? schedule.weeklyEnd ?? schedule.biWeeklyEnd ?? schedule.monthlyEnd ?? '17:00').slice(0, 5));
    const dayIndex = (day: string) => /^\d+$/.test(day) ? Number(day) : DAYS.indexOf(day);
    setWeekDays(schedule.type === 2 ? (schedule.weeklyDays?.length ? schedule.weeklyDays : [schedule.weeklyDay ?? 0]).map(String) : schedule.type === 3 ? [String(schedule.biWeeklyDay ?? 0)] : schedule.type === 1 ? Object.keys(schedule.dailyTimes ?? {}).map(day => String(dayIndex(day))).filter(day => Number(day) >= 0) : []);
    setDailyTimes(Object.fromEntries(Object.entries(schedule.dailyTimes ?? {}).map(([day, range]) => [String(dayIndex(day)), { start: range.start?.slice(0, 5) ?? '08:00', end: range.end?.slice(0, 5) ?? '17:00' }])));
    setWeekParity(String(schedule.biWeeklyWeekParity ?? 1)); setMonthlyDay(schedule.monthlyDay ?? 1);
    setError('');
    const detail = await adminCompanies.get(shift.companyId);
    if (detail) {
      setCompanies(current => current.some(company => company.id === detail.company.id) ? current : [...current, detail.company]);
      setBranches(detail.branches);
    }
    setModalOpen(true);
  };

  const submit = async () => {
    if ((!editingShift || editingShift.validFrom) && !validFrom) { setError('Shift start date is required.'); return; }
    if (validFrom && validUntil && validUntil < validFrom) { setError('End date must be on or after the start date.'); return; }
    if (!companyId || !branchId || !name.trim()) { setError('Company, branch and name are required.'); return; }
    if (scheduleType === '2' && weekDays.length === 0) { setError('Select at least one day.'); return; }
    if (scheduleType === '1' && (!weekDays.length || weekDays.some(day => !dailyTimes[day]?.start || !dailyTimes[day]?.end || dailyTimes[day].end <= dailyTimes[day].start))) { setError('Select at least one day and ensure each end time is after its start time.'); return; }
    if (scheduleType === '3' && weekDays.length === 0) { setError('Select a day.'); return; }
    setSaving(true); setError('');
    const fields = { companyId, branchId, name: name.trim(), schedule: buildSchedule(), notes: notes.trim() || undefined, isActive: editingShift?.isActive ?? true, validFrom: validFrom || undefined, validUntil: validUntil || undefined, qualityCycleTemplateId: qualityCycleTemplateId || undefined };
    const created = editingShift ? await adminShifts.update(editingShift.id, fields) : await adminShifts.create(fields);
    setSaving(false);
    if (!created) { setError(editingShift ? 'Failed to update shift. Check that the company and branch are valid, then retry.' : 'Failed to create shift.'); return; }
    const wasEditing = Boolean(editingShift);
    closeModal(); if (!wasEditing) changeLifecycle('active'); await load();
  };

  const deactivate = async (id: string) => { await adminShifts.deactivate(id); if (assignOpen === id) closeAssignments(); await load(); };
  const reactivate = async (id: string) => {
    setRestoring(id); setListError('');
    try {
      if (!await adminShifts.reactivate(id)) { setListError('Could not reactivate shift. Check that its company and branch are active, then retry.'); return; }
      await load();
    } catch { setListError('Could not reactivate shift. Please retry.'); }
    finally { setRestoring(null); }
  };
  const deleteShift = async (shift: Shift) => {
    if (!window.confirm(`Permanently delete inactive shift “${shift.name}” and remove its employee assignments? Completed checklist records, photos and reports will remain in history.`)) return;
    setRestoring(shift.id); setListError('');
    try { await adminShifts.deleteInactive(shift.id); await load(); }
    catch (failure) { setListError(failure instanceof Error ? failure.message : 'Could not delete shift. Please retry.'); }
    finally { setRestoring(null); }
  };

  const openAssign = async (id: string) => {
    if (assignmentPending.current) return;
    const version = ++assignmentGeneration.current;
    setAssignError(''); setAssignStatus(''); setAssignSel([]);
    if (assignOpen === id) { setAssignOpen(null); setAssignLoading(false); return; }
    setAssignOpen(id); setAssignLoading(true);
    try {
      const a = await adminShifts.assignments(id);
      if (version === assignmentGeneration.current) setAssignments(m => ({ ...m, [id]: a }));
    } catch (e) {
      if (version === assignmentGeneration.current) {
        setAssignments(m => { const next = { ...m }; delete next[id]; return next; });
        setAssignError(e instanceof Error ? e.message : 'Could not load assignments. Please retry.');
      }
    } finally { if (version === assignmentGeneration.current) setAssignLoading(false); }
  };

  const refreshAssignments = async (id: string, version: number) => {
    try {
      const a = await adminShifts.assignments(id);
      if (version === assignmentGeneration.current) setAssignments(m => ({ ...m, [id]: a }));
      return a;
    } catch (e) {
      if (version === assignmentGeneration.current) setAssignments(m => { const next = { ...m }; delete next[id]; return next; });
      throw e;
    }
  };

  const doAssign = async (shiftId: string) => {
    if (assignmentPending.current || assignLoading || !assignSel.length || !assignments[shiftId]) return;
    assignmentPending.current = true; setAssignBusy(true); setAssignError(''); setAssignStatus('');
    const version = assignmentGeneration.current;
    const failed: string[] = []; const messages: string[] = []; let succeeded = 0;
    try {
      for (const empId of assignSel) {
        try { await adminShifts.assign(shiftId, empId); succeeded++; }
        catch (e) { failed.push(empId); messages.push(`${employeeName(empId)}: ${e instanceof Error ? e.message : 'Could not assign employee. Please retry.'}`); }
      }
      let selected = failed;
      try {
        const a = await refreshAssignments(shiftId, version);
        selected = failed.filter(id => !a.some(x => x.isActive && x.employeeId === id));
      } catch { messages.push('Could not refresh assignments. Refresh them before making further changes.'); }
      if (version === assignmentGeneration.current) {
        setAssignSel(selected); setAssignError(messages.join(' '));
        setAssignStatus(succeeded ? `Assigned ${succeeded} employee${succeeded === 1 ? '' : 's'}.${failed.length ? ` ${failed.length} assignment${failed.length === 1 ? '' : 's'} could not be confirmed.` : ''}` : '');
      }
    } finally {
      assignmentPending.current = false;
      if (version === assignmentGeneration.current) setAssignBusy(false);
    }
  };

  const doUnassign = async (shiftId: string, employeeId: string) => {
    if (assignmentPending.current || assignLoading) return;
    assignmentPending.current = true; setAssignBusy(true); setAssignError(''); setAssignStatus('');
    const version = assignmentGeneration.current;
    try {
      await adminShifts.unassign(shiftId, employeeId);
      if (version === assignmentGeneration.current) setAssignStatus(`${employeeName(employeeId)} removed from this shift.`);
      await refreshAssignments(shiftId, version);
    } catch (e) {
      if (version === assignmentGeneration.current) setAssignError(`${employeeName(employeeId)}: ${e instanceof Error ? e.message : 'Could not update assignments. Please retry.'}`);
    } finally {
      assignmentPending.current = false;
      if (version === assignmentGeneration.current) setAssignBusy(false);
    }
  };

  const employeeName = (id: string) => {
    const e = employees.find((x) => x.id === id);
    return e ? `${e.firstName} ${e.lastName}` : id.slice(0, 8);
  };

  const loadOccurrences = async (shift = occurrenceShift) => {
    if (!shift) return;
    setOccurrenceLoading(true); setOccurrenceError('');
    try {
      const fromUtc = tenantLocalToUtc(`${occurrenceFrom}T00:00`, timeZoneId);
      const nextDayUtc = tenantLocalToUtc(`${addCalendarDays(occurrenceTo, 1)}T00:00`, timeZoneId);
      const toUtc = new Date(Date.parse(nextDayUtc) - 1).toISOString();
      const result = await adminShifts.occurrences(shift.id, fromUtc, toUtc);
      if (!result) throw new Error('Could not load occurrences.');
      setOccurrences(result);
    } catch (failure) { setOccurrenceError(failure instanceof Error ? failure.message : 'Could not load occurrences.'); }
    finally { setOccurrenceLoading(false); }
  };

  const openOccurrences = async (shift: Shift) => {
    setOccurrenceShift(shift); setOccurrences([]); setOccurrenceError('');
    await loadOccurrences(shift);
  };

  const openOccurrenceEditor = (occurrence?: ShiftOccurrence, status = '1') => {
    setEditingOccurrence(occurrence ?? { startUtc: '', endUtc: '' });
    setOccurrenceStatus(occurrence?.status ? String(occurrence.status) : status);
    setOccurrenceStart(occurrence?.startUtc ? localInputValue(occurrence.startUtc, timeZoneId) : '');
    setOccurrenceEnd(occurrence?.endUtc ? localInputValue(occurrence.endUtc, timeZoneId) : '');
    setOccurrenceReason(occurrence?.reason ?? ''); setOccurrenceEmployee(occurrence?.employeeId ?? null); setOccurrenceEditError('');
  };

  const saveOccurrence = async () => {
    if (!occurrenceShift || !editingOccurrence || !occurrenceStart || !occurrenceEnd || !occurrenceReason.trim()) {
      setOccurrenceEditError('Start, end and reason are required.'); return;
    }
    setOccurrenceSaving(true); setOccurrenceEditError('');
    try {
      const status = Number(occurrenceStatus);
      await adminShifts.saveOccurrenceException(occurrenceShift.id, {
        id: editingOccurrence.exceptionId,
        originalStartUtc: status === 4 ? null : (editingOccurrence.originalStartUtc ?? editingOccurrence.startUtc),
        startUtc: tenantLocalToUtc(occurrenceStart, timeZoneId), endUtc: tenantLocalToUtc(occurrenceEnd, timeZoneId),
        status, reason: occurrenceReason.trim(), employeeId: occurrenceEmployee,
      });
      setEditingOccurrence(null); await loadOccurrences();
    } catch (failure) { setOccurrenceEditError(failure instanceof Error ? failure.message : 'Could not update this occurrence.'); }
    finally { setOccurrenceSaving(false); }
  };

  const restoreOccurrence = async (occurrence: ShiftOccurrence) => {
    if (!occurrenceShift || !occurrence.exceptionId) return;
    setOccurrenceSaving(true); setOccurrenceError('');
    try { await adminShifts.deleteOccurrenceException(occurrenceShift.id, occurrence.exceptionId); await loadOccurrences(); }
    catch (failure) { setOccurrenceError(failure instanceof Error ? failure.message : 'Could not restore this occurrence.'); }
    finally { setOccurrenceSaving(false); }
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Shifts</Title>
        <Button leftSection={<Plus size={16} />} disabled={!schedulingReady} onClick={openCreate}>Add shift</Button>
      </Group>

      {listError && <Text role="alert" c="red" mb="md">{listError}</Text>}

      <LifecycleTabs value={lifecycle} onChange={changeLifecycle} label="Shift activity" disabled={assignBusy || templateSaving}
        activeCount={activeCount} inactiveCount={inactiveCount}>
      {loading ? <Loader /> : visibleShifts.length === 0 ? (
        <Text c="dimmed">{shifts.length === 0 ? 'No shifts yet.' : `No ${lifecycle} shifts. Check the ${lifecycle === 'active' ? 'Inactive' : 'Active'} tab.`}</Text>
      ) : (
        <Table.ScrollContainer minWidth={600}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Schedule</Table.Th><Table.Th>Employees</Table.Th><Table.Th w={180}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleShifts.map((s) => (
                <Fragment key={s.id}>
                  <Table.Tr>
                    <Table.Td fw={500}>{s.name}</Table.Td>
                    <Table.Td>
                      {s.schedule.type === 0 && `Daily ${s.schedule.dailyStart}–${s.schedule.dailyEnd}`}
                      {s.schedule.type === 1 && `Daily by day (${Object.keys(s.schedule.dailyTimes ?? {}).length} days)`}
                      {s.schedule.type === 2 && (s.schedule.weeklyDays?.length
                        ? `${s.schedule.weeklyDays.map((d) => DAYS[d]).join(', ')} ${s.schedule.weeklyStart}–${s.schedule.weeklyEnd}`
                        : `${DAYS[s.schedule.weeklyDay ?? 0]} ${s.schedule.weeklyStart}–${s.schedule.weeklyEnd}`)}
                      {s.schedule.type === 3 && `Bi-weekly ${DAYS[s.schedule.biWeeklyDay ?? 0]}`}
                      {s.schedule.type === 5 && `Monthly day ${s.schedule.monthlyDay}`}
                      {s.schedule.type === 4 && 'On-call'}
                    </Table.Td>
                    <Table.Td>{assignments[s.id]?.length ?? 0}</Table.Td>
                    <Table.Td>
                      <Group gap={6} wrap="nowrap">
                        {isAdmin() && <Button size="xs" variant="light" onClick={() => { setPricingShift(s); setPricingOccurrence(undefined); }}>Pricing</Button>}
                        {isAdmin() && <Button size="xs" variant="light" leftSection={<Pencil size={13} />} onClick={() => openEdit(s)}>Edit</Button>}
                        {s.isActive && <Button size="xs" variant="light" disabled={assignBusy} onClick={() => openAssign(s.id)}>Employees</Button>}
                        {s.isActive && <Button size="xs" variant="light" onClick={() => openOccurrences(s)}>Occurrences</Button>}
                        {isAdmin() && s.isActive && <Button size="xs" variant="light" onClick={() => { setTemplateShift(s); setAssignedTemplate(s.qualityCycleTemplateId ?? null); setTemplateError(''); }}>Checklist template</Button>}
                        {s.isActive && <Button size="xs" variant="subtle" color="red" disabled={assignBusy} onClick={() => deactivate(s.id)}>Deactivate</Button>}
                        {!s.isActive && <Button size="xs" loading={restoring === s.id} disabled={!!restoring} onClick={() => reactivate(s.id)}>Reactivate</Button>}
                        {!s.isActive && <Button size="xs" color="red" variant="subtle" loading={restoring === s.id} disabled={!!restoring} onClick={() => deleteShift(s)}>Delete</Button>}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                  {assignOpen === s.id && (
                    <Table.Tr key={s.id + '-assign'}>
                      <Table.Td colSpan={4} p="sm">
                        <Group align="flex-end" mb="xs" wrap="wrap" gap="xs">
                          <MultiSelect
                            label="Assign employees"
                            placeholder="Select employees"
                            data={employees.filter(e => e.isActive && !(assignments[s.id] ?? []).some(a => a.isActive && a.employeeId === e.id)).map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
                            value={assignSel}
                            onChange={setAssignSel}
                            style={{ flex: 1, minWidth: 200 }}
                            searchable
                            disabled={assignLoading || assignBusy || !assignments[s.id]}
                          />
                          <Button size="xs" loading={assignBusy} disabled={assignLoading || !assignSel.length || !assignments[s.id]} onClick={() => doAssign(s.id)}>Assign</Button>
                          <Button size="xs" variant="default" disabled={assignLoading || assignBusy} onClick={async () => {
                            const version = ++assignmentGeneration.current;
                            setAssignLoading(true); setAssignError(''); setAssignStatus('');
                            try {
                              const a = await refreshAssignments(s.id, version);
                              if (version === assignmentGeneration.current) setAssignSel(selected => selected.filter(id => !a.some(x => x.isActive && x.employeeId === id)));
                            } catch (e) { if (version === assignmentGeneration.current) setAssignError(e instanceof Error ? e.message : 'Could not load assignments. Please retry.'); }
                            finally { if (version === assignmentGeneration.current) setAssignLoading(false); }
                          }}>Refresh assignments</Button>
                        </Group>
                        <Text size="sm" c="dimmed">Overlapping shifts are checked when you assign employees. Any conflict will be shown here.</Text>
                        {assignError && <Text role="alert" aria-label="Assignment error" c="red">{assignError}</Text>}
                        {assignStatus && <Text role="status">{assignStatus}</Text>}
                        {assignLoading ? <Text>Loading assignments…</Text> : !assignments[s.id] ? <Text>Assignments could not be loaded. Use Refresh assignments to retry.</Text> : assignments[s.id].length === 0 ? (
                          <Text size="sm" c="dimmed">No employees assigned.</Text>
                        ) : (
                          <Stack gap={4}>
                            {(assignments[s.id] ?? []).map((a) => (
                              <Group key={a.id} justify="space-between" wrap="wrap" gap="xs">
                                <Text size="sm">{employeeName(a.employeeId)}</Text>
                                <ActionIcon variant="subtle" color="red" size="sm" disabled={assignBusy || assignLoading} aria-label={`Unassign ${employeeName(a.employeeId)}`} onClick={() => doUnassign(s.id, a.employeeId)}><Trash2 size={14} /></ActionIcon>
                              </Group>
                            ))}
                          </Stack>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Fragment>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      {pricingShift && <ShiftPricingDialog key={`${pricingShift.id}-${pricingOccurrence?.startUtc ?? "plan"}`} shift={pricingShift} occurrence={pricingOccurrence} onClose={() => { setPricingShift(null); setPricingOccurrence(undefined); }} />}
      <Modal opened={Boolean(occurrenceShift)} onClose={() => { setOccurrenceShift(null); setEditingOccurrence(null); }} title={`Occurrences${occurrenceShift ? ` — ${occurrenceShift.name}` : ''}`} size="xl">
        <Stack>
          <Text size="sm" c="dimmed">Times use the tenant time zone: {timeZoneId}. Changes apply only to the selected visit.</Text>
          <Group align="flex-end" wrap="wrap">
            <TextInput label="From" type="date" value={occurrenceFrom} onChange={e => setOccurrenceFrom(e.target.value)} />
            <TextInput label="To" type="date" value={occurrenceTo} onChange={e => setOccurrenceTo(e.target.value)} />
            <Button variant="default" loading={occurrenceLoading} onClick={() => loadOccurrences()}>Load</Button>
            <Button onClick={() => openOccurrenceEditor(undefined, '4')}>Add extra visit</Button>
          </Group>
          {occurrenceError && <Text role="alert" c="red">{occurrenceError}</Text>}
          {occurrenceLoading ? <Loader /> : occurrences.length === 0 ? <Text c="dimmed">No occurrences in this period.</Text> : (
            <Table striped withTableBorder>
              <Table.Thead><Table.Tr><Table.Th>Start</Table.Th><Table.Th>End</Table.Th><Table.Th>Status</Table.Th><Table.Th>Assignees</Table.Th><Table.Th>Reason</Table.Th><Table.Th></Table.Th></Table.Tr></Table.Thead>
              <Table.Tbody>{occurrences.map((item) => <Table.Tr key={item.exceptionId ?? item.startUtc}>
                <Table.Td>{new Date(item.startUtc).toLocaleString(undefined, { timeZone: timeZoneId })}</Table.Td>
                <Table.Td>{new Date(item.endUtc).toLocaleString(undefined, { timeZone: timeZoneId })}</Table.Td>
                <Table.Td>{item.status ? OCCURRENCE_STATUSES.find(x => x.value === String(item.status))?.label : 'Scheduled'}</Table.Td>
                <Table.Td><Text size="sm">{(item.assigneeIds ?? []).map(employeeName).join(', ') || 'Unassigned'}</Text>
                  <Text size="xs" c="dimmed">{item.assignmentSource ?? 'Recurring'}</Text>{item.assignmentLockReason && <Text size="xs" c="dimmed">{item.assignmentLockReason}</Text>}</Table.Td>
                <Table.Td>{item.reason ?? '—'}</Table.Td>
                <Table.Td><Group gap={4} wrap="nowrap">
                  {isAdmin() && occurrenceShift?.schedule.type === 4 && <Button size="xs" variant="light" onClick={() => { setPricingShift(occurrenceShift); setPricingOccurrence(item); }}>Visit price</Button>}
                  <Button size="xs" variant="light" disabled={item.canAssign === false || item.status === 2 || item.status === 3} onClick={() => setAssigningVisit(item)}>Assign</Button>
                  <Button size="xs" variant="subtle" onClick={() => openOccurrenceEditor(item)}>Change</Button>
                  {!item.exceptionId && <Button size="xs" variant="subtle" color="orange" onClick={() => openOccurrenceEditor(item, '2')}>Cancel</Button>}
                  {item.exceptionId && <Button size="xs" variant="subtle" color="red" loading={occurrenceSaving} onClick={() => restoreOccurrence(item)}>{item.status === 4 ? 'Delete extra' : 'Restore'}</Button>}
                </Group></Table.Td>
              </Table.Tr>)}</Table.Tbody>
            </Table>
          )}
        </Stack>
      </Modal>

      {assigningVisit && occurrenceShift && <VisitAssigneesDialog key={`${assigningVisit.exceptionId ?? assigningVisit.startUtc}`}
        shiftId={occurrenceShift.id} visit={assigningVisit} employees={employees} timeZone={timeZoneId}
        onClose={() => setAssigningVisit(null)} onSaved={async () => { await loadOccurrences(); }} />}
      <Modal opened={Boolean(editingOccurrence)} onClose={() => setEditingOccurrence(null)} title="Change occurrence">
        <Stack>
          <Select label="Action" value={occurrenceStatus} onChange={value => setOccurrenceStatus(value ?? '1')} data={OCCURRENCE_STATUSES}
            disabled={Boolean(editingOccurrence?.exceptionId)} />
          <TextInput label={`Start (${timeZoneId})`} type="datetime-local" required value={occurrenceStart} onChange={e => setOccurrenceStart(e.target.value)} />
          <TextInput label={`End (${timeZoneId})`} type="datetime-local" required value={occurrenceEnd} onChange={e => setOccurrenceEnd(e.target.value)} />
          <Select label="Replacement employee (optional)" clearable searchable value={occurrenceEmployee} onChange={setOccurrenceEmployee}
            data={employees.filter(e => e.isActive).map(e => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))} />
          <TextInput label="Reason" required maxLength={500} value={occurrenceReason} onChange={e => setOccurrenceReason(e.target.value)} />
          {occurrenceEditError && <Text role="alert" c="red">{occurrenceEditError}</Text>}
          <Group justify="flex-end"><Button variant="default" onClick={() => setEditingOccurrence(null)}>Cancel</Button><Button loading={occurrenceSaving} onClick={saveOccurrence}>Save occurrence</Button></Group>
        </Stack>
      </Modal>

      <Modal opened={Boolean(templateShift)} onClose={() => setTemplateShift(null)} title="Assign checklist template">
        <Stack>
          <Text>{templateShift?.name}</Text>
          <Select label="Checklist template" placeholder="None" clearable value={assignedTemplate} onChange={setAssignedTemplate}
            data={templates.filter(t => (!t.companyId || t.companyId === templateShift?.companyId) && (!t.branchId || t.branchId === templateShift?.branchId)).map(t => ({ value: t.id, label: t.title }))} />
          <Text size="sm">Applies to upcoming occurrences and the current occurrence if it has no checklist yet. Started and submitted checklists keep their original items.</Text>
          {templateError && <Text c="red" role="alert">{templateError}</Text>}
          <Button loading={templateSaving} onClick={async () => {
            if (!templateShift) return;
            setTemplateSaving(true); setTemplateError('');
            try { const result = await adminShifts.setQualityCycleTemplate(templateShift.id, assignedTemplate);
              if (!result) { setTemplateError('Could not save the template assignment. Please retry.'); return; }
              setTemplateShift(null); await load();
            } catch { setTemplateError('Could not save the template assignment. Please retry.'); }
            finally { setTemplateSaving(false); }
          }}>Save template assignment</Button>
        </Stack>
      </Modal>
      <Modal opened={modalOpen} onClose={closeModal} title={editingShift ? 'Edit shift' : 'Add shift'} size="lg" radius="md">
        <Stack gap="sm">
          <Text size="sm" c="dimmed">Schedule times use the tenant time zone: {timeZoneId}.</Text>
          <Select label="Company" data={companies.filter(c => c.isActive !== false || c.id === editingShift?.companyId).map((c) => ({ value: c.id, label: `${c.name}${c.isActive === false ? ' (inactive)' : ''}` }))} value={companyId} onChange={(v) => { setBranches([]); setCompanyId(v); setBranchId(null); setQualityCycleTemplateId(null); }} searchable />
          <Select label="Branch" data={branches.filter(b => b.isActive !== false || b.id === editingShift?.branchId).map((b) => ({ value: b.id, label: `${b.name}${b.isActive === false ? ' (inactive)' : ''}` }))} value={branchId} onChange={v => { setBranchId(v); setQualityCycleTemplateId(null); }} searchable disabled={!companyId} />
          <TextInput label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <Group grow>
            <TextInput label="Start date" type="date" required={!editingShift || Boolean(editingShift.validFrom)} value={validFrom} onChange={e => setValidFrom(e.target.value)} />
            <TextInput label="End date (optional)" type="date" min={validFrom || undefined} value={validUntil} onChange={e => setValidUntil(e.target.value)} />
          </Group>
          <Select label="Schedule" data={SCHEDULE_TYPES} value={scheduleType} onChange={(v) => setScheduleType(v ?? '0')} />

          {scheduleType === '2' && (
            <MultiSelect
              label="Days"
              data={DAYS.map((d, i) => ({ value: String(i), label: d }))}
              value={weekDays}
              onChange={setWeekDays}
            />
          )}
          {scheduleType === '1' && <>
            <MultiSelect label="Days" data={DAYS.map((d, i) => ({ value: String(i), label: d }))} value={weekDays} onChange={setWeekDays} />
            {weekDays.map(day => <Group key={day} wrap="wrap" grow gap="xs">
              <Text size="sm" fw={500}>{DAYS[Number(day)]}</Text>
              <TextInput label="Start" type="time" value={dailyTimes[day]?.start ?? '08:00'} onChange={e => setDailyTimes(current => ({ ...current, [day]: { ...(current[day] ?? { start: '08:00', end: '17:00' }), start: e.target.value } }))} />
              <TextInput label="End" type="time" value={dailyTimes[day]?.end ?? '17:00'} onChange={e => setDailyTimes(current => ({ ...current, [day]: { ...(current[day] ?? { start: '08:00', end: '17:00' }), end: e.target.value } }))} />
            </Group>)}
          </>}
          {scheduleType === '3' && (
            <Group wrap="wrap" grow gap="xs">
              <Select label="Week parity" data={[{ value: '1', label: 'Week 1' }, { value: '2', label: 'Week 2' }]} value={weekParity} onChange={(v) => setWeekParity(v ?? '1')} />
              <Select label="Day" data={DAYS.map((d, i) => ({ value: String(i), label: d }))} value={weekDays[0] ?? ''} onChange={(v) => setWeekDays(v ? [v] : [])} />
            </Group>
          )}
          {scheduleType === '5' && (
            <NumberInput label="Day of month (1-31)" min={1} max={31} value={monthlyDay} onChange={setMonthlyDay} />
          )}
          {scheduleType !== '4' && scheduleType !== '1' && (
            <Group wrap="wrap" grow gap="xs">
              <TextInput label="Start" type="time" value={start} onChange={(e) => setStart(e.target.value)} />
              <TextInput label="End" type="time" value={end} onChange={(e) => setEnd(e.target.value)} />
            </Group>
          )}
          <TextInput label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
          <Select
            label="Quality cycle template"
            placeholder="None"
            clearable
            data={templates.filter(t => (!t.companyId || t.companyId === companyId) && (!t.branchId || t.branchId === branchId)).map((t) => ({ value: t.id, label: t.title }))}
            value={qualityCycleTemplateId}
            onChange={setQualityCycleTemplateId}
            searchable
          />
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={closeModal}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingShift ? 'Save changes' : 'Create shift'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
