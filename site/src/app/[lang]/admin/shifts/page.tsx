'use client';

import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge } from '@mantine/core';
import { Plus } from 'lucide-react';
import { adminShifts, adminCompanies, adminBranches, type Shift, type Company, type Branch } from '@/lib/adminApi';

export default function ShiftsAdminPage() {
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [loading, setLoading] = useState(true);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [modalOpen, setModalOpen] = useState(false);

  const [companyId, setCompanyId] = useState<string | null>(null);
  const [branchId, setBranchId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [dailyStart, setDailyStart] = useState('08:00');
  const [dailyEnd, setDailyEnd] = useState('17:00');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = () => adminShifts.list().then((r) => { if (r) setShifts(r); setLoading(false); });

  useEffect(() => {
    load();
    adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); });
  }, []);

  useEffect(() => {
    if (!companyId) { setBranches([]); return; }
    adminCompanies.get(companyId).then((d) => { if (d) setBranches(d.branches); });
  }, [companyId]);

  const submit = async () => {
    if (!companyId || !branchId || !name.trim()) { setError('Company, branch and name are required.'); return; }
    setSaving(true); setError('');
    const created = await adminShifts.create({
      companyId, branchId, name: name.trim(),
      schedule: { type: 0, dailyStart, dailyEnd },
      notes: notes.trim() || undefined,
    });
    setSaving(false);
    if (!created) { setError('Failed to create shift.'); return; }
    setName(''); setNotes(''); setModalOpen(false); await load();
  };

  const deactivate = async (id: string) => { await adminShifts.deactivate(id); await load(); };

  return (
    <>
      <Group justify="space-between" mb="md">
        <Title order={2}>Shifts</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setModalOpen(true)}>Add shift</Button>
      </Group>

      {loading ? <Loader /> : shifts.length === 0 ? (
        <Text c="dimmed">No shifts yet.</Text>
      ) : (
        <Table striped highlightOnHover withTableBorder>
          <Table.Thead>
            <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Schedule</Table.Th><Table.Th w={120}>Status</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {shifts.map((s) => (
              <Table.Tr key={s.id}>
                <Table.Td fw={500}>{s.name}</Table.Td>
                <Table.Td>{s.schedule.type === 0 ? `${s.schedule.dailyStart ?? ''} – ${s.schedule.dailyEnd ?? ''}` : `type ${s.schedule.type}`}</Table.Td>
                <Table.Td>
                  {s.isActive
                    ? <Button size="xs" variant="subtle" color="red" onClick={() => deactivate(s.id)}>Deactivate</Button>
                    : <Badge color="gray" size="sm">Inactive</Badge>}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Add shift">
        <Stack>
          <Select label="Company" data={companies.map((c) => ({ value: c.id, label: c.name }))} value={companyId} onChange={(v) => { setCompanyId(v); setBranchId(null); }} searchable />
          <Select label="Branch" data={branches.map((b) => ({ value: b.id, label: b.name }))} value={branchId} onChange={setBranchId} searchable disabled={!companyId} />
          <TextInput label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <Group grow>
            <TextInput label="Start" type="time" value={dailyStart} onChange={(e) => setDailyStart(e.target.value)} />
            <TextInput label="End" type="time" value={dailyEnd} onChange={(e) => setDailyEnd(e.target.value)} />
          </Group>
          <TextInput label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>Create shift</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
