'use client';

import { Fragment, useEffect, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Select, MultiSelect, Group, Stack, Text, Loader, ActionIcon, NumberInput,
} from '@mantine/core';
import { Plus, Trash2 } from 'lucide-react';
import {
  adminShifts, adminCompanies, adminEmployees,
  type Shift, type Company, type Branch, type Employee, type ShiftAssignment,
} from '@/lib/adminApi';

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
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const [assignOpen, setAssignOpen] = useState<string | null>(null);
  const [assignments, setAssignments] = useState<Record<string, ShiftAssignment[]>>({});
  const [assignSel, setAssignSel] = useState<string[]>([]);

  const load = () => adminShifts.list().then((r) => { if (r) setShifts(r); setLoading(false); });

  useEffect(() => {
    load();
    adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); });
    adminEmployees.list().then((e) => { if (e) setEmployees(e); });
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
    setName(''); setNotes(''); setWeekDays([]); setCompanyId(null); setBranchId(null); setError(''); setModalOpen(false);
  };

  const submit = async () => {
    if (!companyId || !branchId || !name.trim()) { setError('Company, branch and name are required.'); return; }
    if (scheduleType === '2' && weekDays.length === 0) { setError('Select at least one day.'); return; }
    if (scheduleType === '3' && weekDays.length === 0) { setError('Select a day.'); return; }
    setSaving(true); setError('');
    const created = await adminShifts.create({ companyId, branchId, name: name.trim(), schedule: buildSchedule(), notes: notes.trim() || undefined });
    setSaving(false);
    if (!created) { setError('Failed to create shift.'); return; }
    closeModal(); await load();
  };

  const deactivate = async (id: string) => { await adminShifts.deactivate(id); await load(); };

  const openAssign = async (id: string) => {
    if (assignOpen === id) { setAssignOpen(null); return; }
    setAssignOpen(id); setAssignSel([]);
    const a = await adminShifts.assignments(id);
    if (a) setAssignments((m) => ({ ...m, [id]: a }));
  };

  const doAssign = async (shiftId: string) => {
    for (const empId of assignSel) await adminShifts.assign(shiftId, empId);
    setAssignSel([]);
    const a = await adminShifts.assignments(shiftId);
    if (a) setAssignments((m) => ({ ...m, [shiftId]: a }));
  };

  const doUnassign = async (shiftId: string, assignmentId: string) => {
    await adminShifts.unassign(shiftId, assignmentId);
    const a = await adminShifts.assignments(shiftId);
    if (a) setAssignments((m) => ({ ...m, [shiftId]: a }));
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

      {loading ? <Loader /> : shifts.length === 0 ? (
        <Text c="dimmed">No shifts yet.</Text>
      ) : (
        <Table.ScrollContainer minWidth={600}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Schedule</Table.Th><Table.Th>Employees</Table.Th><Table.Th w={180}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {shifts.map((s) => (
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
                        <Button size="xs" variant="light" onClick={() => openAssign(s.id)}>Employees</Button>
                        {s.isActive && <Button size="xs" variant="subtle" color="red" onClick={() => deactivate(s.id)}>Deactivate</Button>}
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
                            data={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
                            value={assignSel}
                            onChange={setAssignSel}
                            style={{ flex: 1, minWidth: 200 }}
                            searchable
                          />
                          <Button size="xs" onClick={() => doAssign(s.id)}>Assign</Button>
                        </Group>
                        {(assignments[s.id] ?? []).length === 0 ? (
                          <Text size="sm" c="dimmed">No employees assigned.</Text>
                        ) : (
                          <Stack gap={4}>
                            {(assignments[s.id] ?? []).map((a) => (
                              <Group key={a.id} justify="space-between" wrap="wrap" gap="xs">
                                <Text size="sm">{employeeName(a.employeeId)}</Text>
                                <ActionIcon variant="subtle" color="red" size="sm" onClick={() => doUnassign(s.id, a.employeeId)}><Trash2 size={14} /></ActionIcon>
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

      <Modal opened={modalOpen} onClose={closeModal} title="Add shift" size="lg" radius="md">
        <Stack gap="sm">
          <Select label="Company" data={companies.map((c) => ({ value: c.id, label: c.name }))} value={companyId} onChange={(v) => { setCompanyId(v); setBranchId(null); }} searchable />
          <Select label="Branch" data={branches.map((b) => ({ value: b.id, label: b.name }))} value={branchId} onChange={setBranchId} searchable disabled={!companyId} />
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
