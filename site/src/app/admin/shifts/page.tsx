'use client';

import { Fragment, useEffect, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Select, MultiSelect, Group, Stack, Text, Loader, ActionIcon, NumberInput,
} from '@mantine/core';
import { Plus, Trash2 } from 'lucide-react';
import {
  adminShifts, adminCompanies, adminEmployees,
  type Shift, type Company, type Branch, type Employee, type ShiftAssignment,
} from '@/lib/adminApi';
import { isAdmin } from '@/lib/auth';
import { adminQualityCycle, type QualityCycleTemplate } from '@/lib/qualityCycleApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const SCHEDULE_TYPES = [
  { value: '0', label: 'Daily' },
  { value: '2', label: 'Specific days' },
  { value: '3', label: 'Bi-weekly' },
  { value: '5', label: 'Monthly' },
  { value: '4', label: 'On-call / flexible' },
];

export default function ShiftsAdminPage() {
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [loading, setLoading] = useState(true);
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const [listError, setListError] = useState('');
  const [restoring, setRestoring] = useState<string | null>(null);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [modalOpen, setModalOpen] = useState(false);

  const [companyId, setCompanyId] = useState<string | null>(null);
  const [branchId, setBranchId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [scheduleType, setScheduleType] = useState('0');
  const [start, setStart] = useState('08:00');
  const [end, setEnd] = useState('17:00');
  const [weekDays, setWeekDays] = useState<string[]>([]);
  const [weekParity, setWeekParity] = useState('1');
  const [monthlyDay, setMonthlyDay] = useState<string | number>(1);
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
  useEffect(() => () => { assignmentGeneration.current++; }, []);

  const load = () => adminShifts.list(undefined, undefined, true).then((r) => { if (r) setShifts(r); setLoading(false); });
  useEffect(() => { load(); }, []);

  const closeAssignments = () => {
    assignmentGeneration.current++;
    setAssignOpen(null); setAssignSel([]); setAssignLoading(false);
    setAssignError(''); setAssignStatus('');
  };
  const changeLifecycle = (status: LifecycleStatus) => {
    if (status === lifecycle) return;
    closeAssignments(); setTemplateShift(null); setLifecycle(status);
  };
  const visibleShifts = shifts.filter(s => inLifecycle(s, lifecycle));

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
    setName(''); setNotes(''); setWeekDays([]); setCompanyId(null); setBranchId(null); setQualityCycleTemplateId(null); setError(''); setModalOpen(false);
  };

  const submit = async () => {
    if (!companyId || !branchId || !name.trim()) { setError('Company, branch and name are required.'); return; }
    if (scheduleType === '2' && weekDays.length === 0) { setError('Select at least one day.'); return; }
    if (scheduleType === '3' && weekDays.length === 0) { setError('Select a day.'); return; }
    setSaving(true); setError('');
    const created = await adminShifts.create({ companyId, branchId, name: name.trim(), schedule: buildSchedule(), notes: notes.trim() || undefined, qualityCycleTemplateId: qualityCycleTemplateId || undefined });
    setSaving(false);
    if (!created) { setError('Failed to create shift.'); return; }
    closeModal(); changeLifecycle('active'); await load();
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

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Shifts</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setModalOpen(true)}>Add shift</Button>
      </Group>

      {listError && <Text role="alert" c="red" mb="md">{listError}</Text>}

      <LifecycleTabs value={lifecycle} onChange={changeLifecycle} label="Shift activity" disabled={assignBusy || templateSaving}
        activeCount={loading ? undefined : shifts.filter(s => s.isActive).length} inactiveCount={loading ? undefined : shifts.filter(s => !s.isActive).length}>
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
                        {s.isActive && <Button size="xs" variant="light" disabled={assignBusy} onClick={() => openAssign(s.id)}>Employees</Button>}
                        {isAdmin() && s.isActive && <Button size="xs" variant="light" onClick={() => { setTemplateShift(s); setAssignedTemplate(s.qualityCycleTemplateId ?? null); setTemplateError(''); }}>Checklist template</Button>}
                        {s.isActive && <Button size="xs" variant="subtle" color="red" disabled={assignBusy} onClick={() => deactivate(s.id)}>Deactivate</Button>}
                        {!s.isActive && <Button size="xs" loading={restoring === s.id} disabled={!!restoring} onClick={() => reactivate(s.id)}>Reactivate</Button>}
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
      <Modal opened={modalOpen} onClose={closeModal} title="Add shift" size="lg" radius="md">
        <Stack gap="sm">
          <Select label="Company" data={companies.map((c) => ({ value: c.id, label: c.name }))} value={companyId} onChange={(v) => { setCompanyId(v); setBranchId(null); setQualityCycleTemplateId(null); }} searchable />
          <Select label="Branch" data={branches.map((b) => ({ value: b.id, label: b.name }))} value={branchId} onChange={v => { setBranchId(v); setQualityCycleTemplateId(null); }} searchable disabled={!companyId} />
          <TextInput label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <Select label="Schedule" data={SCHEDULE_TYPES} value={scheduleType} onChange={(v) => setScheduleType(v ?? '0')} />

          {scheduleType === '2' && (
            <MultiSelect
              label="Days"
              data={DAYS.map((d, i) => ({ value: String(i), label: d }))}
              value={weekDays}
              onChange={setWeekDays}
            />
          )}
          {scheduleType === '3' && (
            <Group wrap="wrap" grow gap="xs">
              <Select label="Week parity" data={[{ value: '1', label: 'Week 1' }, { value: '2', label: 'Week 2' }]} value={weekParity} onChange={(v) => setWeekParity(v ?? '1')} />
              <Select label="Day" data={DAYS.map((d, i) => ({ value: String(i), label: d }))} value={weekDays[0] ?? ''} onChange={(v) => setWeekDays(v ? [v] : [])} />
            </Group>
          )}
          {scheduleType === '5' && (
            <NumberInput label="Day of month (1-31)" min={1} max={31} value={monthlyDay} onChange={setMonthlyDay} />
          )}
          {scheduleType !== '4' && (
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
            <Button loading={saving} onClick={submit}>Create shift</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
