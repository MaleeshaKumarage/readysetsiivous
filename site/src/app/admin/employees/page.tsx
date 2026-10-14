'use client';

import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge } from '@mantine/core';
import { isAdmin } from '@/lib/auth';
import { Plus } from 'lucide-react';
import { adminEmployees, type Employee } from '@/lib/adminApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

const ROLES = ['admin', 'manager', 'employee'];

export default function EmployeesAdminPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const visibleEmployees = employees.filter(employee => inLifecycle(employee, lifecycle));
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState({ email: '', firstName: '', lastName: '', phone: '', role: 'employee' });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [retryEmployee, setRetryEmployee] = useState<Employee | null>(null);
  const [restoring, setRestoring] = useState<string | null>(null);
  const [inviteBusy, setInviteBusy] = useState<string | null>(null);
  const [inviteMessage, setInviteMessage] = useState('');
  const [inviteFailure, setInviteFailure] = useState('');

  const load = () => adminEmployees.list(true).then((r) => { if (r) setEmployees(r); setLoading(false); });
  useEffect(() => { load(); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.email.trim() || !form.firstName.trim() || !form.lastName.trim()) { setError('Email, first and last name are required.'); return; }
    setSaving(true); setError('');
    try {
    const created = await adminEmployees.create({
      email: form.email.trim(),
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      phone: form.phone.trim(),
      role: form.role,
      skills: [],
      serviceAreas: [],
      colorHex: null,
      payRate: null,
      notes: null,
      certifications: [],
      defaultHours: {},
    });
    setSaving(false);
    if (!created) { setError('Failed to create employee.'); return; }
    setLifecycle('active');
    setForm({ email: '', firstName: '', lastName: '', phone: '', role: 'employee' });
    setModalOpen(false); await load();
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Could not create employee. Please retry.'); }
    finally { setSaving(false); }
  };

  const deactivate = async (id: string) => {
    setInviteFailure('');
    try { if (!await adminEmployees.deactivate(id)) setInviteFailure('Employee access update could not be confirmed. Reload and retry or contact an administrator.'); await load(); }
    catch { setInviteFailure('Employee access update could not be confirmed. Please retry.'); }
  };

  const reactivate = async (employee: Employee) => {
    setRestoring(employee.id); setInviteFailure(''); setInviteMessage('');
    try {
      await adminEmployees.reactivate(employee.id);
      setRetryEmployee(null);
      await load();
      setInviteMessage(`${employee.firstName} ${employee.lastName} reactivated. Existing account and history preserved.`);
    } catch (failure) {
      setRetryEmployee(employee);
      setInviteFailure(failure instanceof Error ? failure.message : 'Could not reactivate employee. Please retry.');
      await load();
    } finally { setRestoring(null); }
  };

  const invite = async (employee: Employee) => {
    setInviteBusy(employee.id); setInviteMessage(''); setInviteFailure('');
    try {
      const result = await adminEmployees.invite(employee.id);
      setInviteMessage(result.status === 'AlreadySent' ? `A setup email was sent recently to ${result.email}. Please check the inbox.` : `Account setup email sent to ${result.email}.`);
      await load();
    } catch (error) { setInviteFailure(error instanceof Error ? error.message : 'Could not send the setup email. Please retry.'); }
    finally { setInviteBusy(null); }
  };
  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Employees</Title>
        {isAdmin() && <Button leftSection={<Plus size={16} />} onClick={() => setModalOpen(true)}>Add employee</Button>}
      </Group>

      {inviteMessage && <Text role="status" c="green" mb="sm">{inviteMessage}</Text>}
      {inviteFailure && <Text role="alert" c="red" mb="sm">{inviteFailure}</Text>}
      {retryEmployee && !(lifecycle === 'active' && employees.some(employee => employee.id === retryEmployee.id && employee.isActive && employee.identitySyncPending)) && <Button mb="sm" loading={restoring === retryEmployee.id} disabled={!!restoring} onClick={() => reactivate(retryEmployee)}>Retry reactivation</Button>}
      <LifecycleTabs value={lifecycle} onChange={setLifecycle} label="Employee activity"
        activeCount={loading ? undefined : employees.filter(employee => employee.isActive).length}
        inactiveCount={loading ? undefined : employees.filter(employee => !employee.isActive).length}>
      {loading ? <Loader /> : employees.length === 0 ? (
        <Text c="dimmed">No employees yet.</Text>
      ) : visibleEmployees.length === 0 ? (
        <Text c="dimmed">No {lifecycle} employees. Check the {lifecycle === 'active' ? 'Inactive' : 'Active'} tab.</Text>
      ) : (
        <Table.ScrollContainer minWidth={550}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Email</Table.Th><Table.Th>Role</Table.Th><Table.Th>Status</Table.Th><Table.Th>Registration</Table.Th><Table.Th w={120}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleEmployees.map((e) => (
                <Table.Tr key={e.id}>
                  <Table.Td fw={500}>{e.firstName} {e.lastName}</Table.Td>
                  <Table.Td>{e.email}</Table.Td>
                  <Table.Td>{e.role}</Table.Td>
                  <Table.Td>{e.isActive ? <Badge color="green">Active</Badge> : <Badge color="gray">Inactive</Badge>}</Table.Td>
                  <Table.Td>{e.keycloakUserId ? <Badge color="green">Invited</Badge> : <Badge color="gray">Not invited</Badge>}</Table.Td>
                  <Table.Td><Group gap="xs">
                    {isAdmin() && e.isActive && <Button size="xs" variant="light" loading={inviteBusy === e.id} disabled={Boolean(inviteBusy && inviteBusy !== e.id)} onClick={() => invite(e)}>{e.keycloakUserId ? 'Resend setup email' : 'Invite'}</Button>}
                    {isAdmin() && e.isActive && e.identitySyncPending && <Button size="xs" loading={restoring === e.id} disabled={!!restoring} onClick={() => reactivate(e)}>Retry reactivation</Button>}
                    {isAdmin() && !e.isActive && <Button size="xs" loading={restoring === e.id} disabled={!!restoring} onClick={() => reactivate(e)}>Reactivate</Button>}
                    {isAdmin() && e.isActive && <Button size="xs" variant="subtle" color="red" onClick={() => deactivate(e.id)}>Deactivate</Button>}
                  </Group></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Add employee" radius="md">
        <Stack gap="sm">
          <TextInput label="Email" required type="email" value={form.email} onChange={set('email')} />
          <Group wrap="wrap" grow gap="xs">
            <TextInput label="First name" required value={form.firstName} onChange={set('firstName')} />
            <TextInput label="Last name" required value={form.lastName} onChange={set('lastName')} />
          </Group>
          <TextInput label="Phone" value={form.phone} onChange={set('phone')} />
          <Select label="Role" data={ROLES} value={form.role} onChange={(v) => setForm((f) => ({ ...f, role: v ?? 'employee' }))} />
          {error && <Text role="alert" size="sm" c="red">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>Create employee</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
