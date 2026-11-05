'use client';

import { showError } from '@/lib/toast';
import { useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge, TagsInput, NumberInput, Textarea } from '@mantine/core';
import { isAdmin } from '@/lib/auth';
import { Plus } from 'lucide-react';
import { adminEmployees, adminCompanies, authorizedFetch, invalidateAdminCache, type Company, type Employee } from '@/lib/adminApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

const ROLES = ['admin', 'manager', 'employee', 'partner'];

// `source` keeps the certification as the API returned it, so an unedited expiry keeps its time of day and any extra fields.
type CertificationDraft = { name: string; expiresAt: string; source?: Record<string, unknown> & { expiresAtUtc?: string | null } };

const WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
type HoursDraft = Record<string, { start: string; end: string }>;

// Keeps default-hours entries for days the editor does not show, and rewrites the weekday entries from the form.
function buildDefaultHours(original: Employee['defaultHours'], hours: HoursDraft) {
  const next: Record<string, { start?: string | null; end?: string | null }> = {};
  for (const [day, value] of Object.entries(original)) if (!WEEKDAYS.includes(day)) next[day] = value;
  for (const day of WEEKDAYS) {
    const entry = hours[day];
    if (entry && entry.start.trim() && entry.end.trim()) next[day] = { start: `${entry.start.trim()}:00`, end: `${entry.end.trim()}:00` };
  }
  return next;
}

export default function EmployeesAdminPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [companyError, setCompanyError] = useState('');
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const visibleEmployees = employees.filter(employee => inLifecycle(employee, lifecycle));
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingEmployee, setEditingEmployee] = useState<Employee | null>(null);
  const [form, setForm] = useState({ email: '', firstName: '', lastName: '', phone: '', role: 'employee', partnerCompanyId: '' });
  const emptyProfile = { skills: [] as string[], serviceAreas: [] as string[], colorHex: '', payRate: '' as number | '', notes: '', certifications: [] as CertificationDraft[], hours: {} as HoursDraft };
  const [profile, setProfile] = useState(emptyProfile);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [retryEmployee, setRetryEmployee] = useState<Employee | null>(null);
  const [restoring, setRestoring] = useState<string | null>(null);
  const [inviteBusy, setInviteBusy] = useState<string | null>(null);
  const [inviteMessage, setInviteMessage] = useState('');
  const [inviteFailure, setInviteFailure] = useState('');
  const [calendarBusy, setCalendarBusy] = useState<string | null>(null);
  const changeCalendarAccess = async (employee: Employee) => {
    setCalendarBusy(employee.id); setInviteFailure('');
    try {
      const response = await authorizedFetch(`/api/v1/admin/employees/${employee.id}/shift-calendar-access`, {
        method: 'PUT', body: JSON.stringify({ enabled: !employee.shiftCalendarEnabled }), cache: 'no-store',
      });
      if (!response.ok) throw new Error('Could not change calendar access. Reload and retry.');
      const result = await response.json();
      invalidateAdminCache();
      window.dispatchEvent(new Event('admin-calendar-access-changed'));
      setEmployees(current => current.map(item => item.id === employee.id ? { ...item, shiftCalendarEnabled: result.enabled } : item));
      setInviteMessage(`Shift calendar access ${result.enabled ? 'enabled' : 'disabled'} for ${employee.firstName} ${employee.lastName}.`);
    } catch (failure) { setInviteFailure(failure instanceof Error ? failure.message : 'Could not change calendar access.'); }
    finally { setCalendarBusy(null); }
  };

  const [listError, setListError] = useState('');
  // A failed refresh keeps the rows already on screen and shows the error above them.
  const load = () => adminEmployees.list(true).then((r) => {
    if (r) { setEmployees(r); setListError(''); }
    else setListError('Employees could not be loaded. Reload and retry.');
    setLoading(false);
  });
  useEffect(() => { load(); adminCompanies.listAll().then(records => { if (records) setCompanies(records); else setCompanyError('Company selection could not be loaded. Refresh before creating a partner.'); }); }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.email.trim() || !form.firstName.trim() || !form.lastName.trim()) { setError('Email, first and last name are required.'); return; }
    if (form.role === 'partner' && !companies.some(c => c.id === form.partnerCompanyId && c.isActive && c.isPartnerEnabled)) { setError('Select an active company with partner access enabled.'); return; }
    const colour = profile.colorHex.trim();
    if (colour && !/^#[0-9a-fA-F]{6}$/.test(colour)) { setError('Colour must be a 6-digit hex value such as #1a2b3c.'); return; }
    if (profile.payRate !== '' && (!Number.isFinite(Number(profile.payRate)) || Number(profile.payRate) < 0)) { setError('Pay rate must be zero or more.'); return; }
    if (profile.certifications.some(c => !c.name.trim())) { setError('Each certification needs a name. Remove empty rows before saving.'); return; }
    for (const day of WEEKDAYS) {
      const entry = profile.hours[day];
      const start = entry?.start.trim() ?? '';
      const end = entry?.end.trim() ?? '';
      if (!start && !end) continue;
      if (!start || !end) { setError(`${day}: enter both a start and an end time, or clear both.`); return; }
      if (end <= start) { setError(`${day}: the end time must be after the start time.`); return; }
    }
    setSaving(true); setError('');
    try {
    const fields = {
      email: form.email.trim(),
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      phone: form.phone.trim(),
      role: form.role,
      partnerCompanyId: form.role === 'partner' ? form.partnerCompanyId : null,
      skills: profile.skills,
      serviceAreas: profile.serviceAreas,
      colorHex: colour || null,
      payRate: profile.payRate === '' ? null : Number(profile.payRate),
      notes: profile.notes.trim() || null,
      certifications: profile.certifications.map(c => {
        const original = c.source?.expiresAtUtc ?? null;
        const dateUnchanged = c.source !== undefined && c.expiresAt === (original ? original.slice(0, 10) : '');
        return { ...(c.source ?? {}), name: c.name.trim(), expiresAtUtc: dateUnchanged ? original : (c.expiresAt ? `${c.expiresAt}T00:00:00Z` : null) };
      }),
      defaultHours: buildDefaultHours(editingEmployee?.defaultHours ?? {}, profile.hours),
    };
    if (editingEmployee) {
      const result = await adminEmployees.update(editingEmployee.id, fields, editingEmployee.isActive);
      if (!result.ok) throw new Error(result.message);
      setModalOpen(false); setEditingEmployee(null); await load();
      return;
    }
    const created = await adminEmployees.create(fields);
    setSaving(false);
    if (!created) { setError('Failed to create employee.'); return; }
    setLifecycle('active');
    setForm({ email: '', firstName: '', lastName: '', phone: '', role: 'employee', partnerCompanyId: '' });
    setModalOpen(false); await load();
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Could not save the employee. Check the fields, then save again.';
      setError(message); showError(message, editingEmployee ? 'Employee not updated' : 'Employee not created');
    }
    finally { setSaving(false); }
  };

  const openEdit = (employee: Employee) => {
    setEditingEmployee(employee);
    setForm({ email: employee.email, firstName: employee.firstName, lastName: employee.lastName, phone: employee.phone ?? '', role: employee.role, partnerCompanyId: employee.partnerCompanyId ?? '' });
    setProfile({
      skills: employee.skills ?? [],
      serviceAreas: employee.serviceAreas ?? [],
      colorHex: employee.colorHex ?? '',
      payRate: employee.payRate ?? '',
      notes: employee.notes ?? '',
      certifications: (employee.certifications ?? []).map(c => ({ name: c.name, expiresAt: c.expiresAtUtc ? c.expiresAtUtc.slice(0, 10) : '', source: { ...c } })),
      hours: Object.fromEntries(WEEKDAYS.filter(day => employee.defaultHours?.[day]).map(day => [day, { start: (employee.defaultHours[day].start ?? '').slice(0, 5), end: (employee.defaultHours[day].end ?? '').slice(0, 5) }])),
    });
    setError(''); setModalOpen(true);
  };

  const deactivate = async (id: string) => {
    setInviteFailure('');
    try {
      const result = await adminEmployees.deactivate(id);
      // A refusal (for example, the cleaner still has active shifts) is shown as the server explains it.
      // A server error may or may not have saved the change, so the user is told to check before retrying.
      if (!result.ok) setInviteFailure(result.status >= 500 ? `${result.message} Reload to check whether the change was saved before retrying.` : result.message);
      await load();
    }
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
  const deleteEmployee = async (employee: Employee) => {
    if (!window.confirm(`Permanently delete ${employee.firstName} ${employee.lastName}'s inactive profile? Profiles with a login or work history are retained.`)) return;
    setRestoring(employee.id); setInviteFailure('');
    try { await adminEmployees.deleteInactive(employee.id); await load(); }
    catch (failure) { setInviteFailure(failure instanceof Error ? failure.message : 'Could not delete employee. Please retry.'); }
    finally { setRestoring(null); }
  };
  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2} component="h1">Employees</Title>
        {isAdmin() && <Button leftSection={<Plus size={16} />} onClick={() => { setEditingEmployee(null); setForm({ email: '', firstName: '', lastName: '', phone: '', role: 'employee', partnerCompanyId: '' }); setProfile(emptyProfile); setError(''); setModalOpen(true); }}>Add employee</Button>}
      </Group>

      {inviteMessage && <Text role="status" c="green" mb="sm">{inviteMessage}</Text>}
      {inviteFailure && <Text role="alert" c="red" mb="sm">{inviteFailure}</Text>}
      {retryEmployee && !(lifecycle === 'active' && employees.some(employee => employee.id === retryEmployee.id && employee.isActive && employee.identitySyncPending)) && <Button mb="sm" loading={restoring === retryEmployee.id} disabled={!!restoring} onClick={() => reactivate(retryEmployee)}>Retry reactivation</Button>}
      <LifecycleTabs value={lifecycle} onChange={setLifecycle} label="Employee activity"
        activeCount={loading ? undefined : employees.filter(employee => employee.isActive).length}
        inactiveCount={loading ? undefined : employees.filter(employee => !employee.isActive).length}>
      {listError && <Text role="alert" c="red" mb="sm">{listError}</Text>}
      {loading ? <Loader /> : employees.length === 0 && listError ? null : employees.length === 0 ? (
        <Text c="dimmed">No employees yet.</Text>
      ) : visibleEmployees.length === 0 ? (
        <Text c="dimmed">No {lifecycle} employees. Check the {lifecycle === 'active' ? 'Inactive' : 'Active'} tab.</Text>
      ) : (
        <Table.ScrollContainer minWidth={550}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Email</Table.Th><Table.Th>Role / company</Table.Th><Table.Th>Status</Table.Th><Table.Th>Registration</Table.Th><Table.Th w={120}></Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleEmployees.map((e) => (
                <Table.Tr key={e.id}>
                  <Table.Td fw={500}>{e.firstName} {e.lastName}</Table.Td>
                  <Table.Td>{e.email}</Table.Td>
                  <Table.Td>{e.role}{e.role === 'partner' && <Text size="xs">{companies.find(c => c.id === e.partnerCompanyId)?.name ?? 'Company unavailable'}</Text>}</Table.Td>
                  <Table.Td>{e.isActive ? <Badge color="green">Active</Badge> : <Badge color="gray">Inactive</Badge>}</Table.Td>
                  <Table.Td>{e.keycloakUserId ? <Badge color="green">Invited</Badge> : <Badge color="gray">Not invited</Badge>}</Table.Td>
                  <Table.Td><Group gap="xs" wrap="wrap">
                    {isAdmin() && e.role === 'admin' && e.isActive && !e.identitySyncPending && <Button size="xs" variant="light" loading={calendarBusy === e.id} disabled={!!calendarBusy} onClick={() => changeCalendarAccess(e)}>{e.shiftCalendarEnabled ? 'Disable shift calendar' : 'Enable shift calendar'}</Button>}
                    {isAdmin() && <Button size="xs" variant="light" onClick={() => openEdit(e)}>Edit</Button>}
                    {isAdmin() && e.isActive && <Button size="xs" variant="light" loading={inviteBusy === e.id} disabled={Boolean(inviteBusy && inviteBusy !== e.id)} onClick={() => invite(e)}>{e.keycloakUserId ? 'Resend setup email' : 'Invite'}</Button>}
                    {isAdmin() && e.isActive && e.identitySyncPending && <Button size="xs" loading={restoring === e.id} disabled={!!restoring} onClick={() => reactivate(e)}>Retry reactivation</Button>}
                    {isAdmin() && !e.isActive && <Button size="xs" loading={restoring === e.id} disabled={!!restoring} onClick={() => reactivate(e)}>Reactivate</Button>}
                    {isAdmin() && !e.isActive && <Button size="xs" color="red" variant="subtle" loading={restoring === e.id} disabled={!!restoring} onClick={() => deleteEmployee(e)}>Delete</Button>}
                    {isAdmin() && e.isActive && <Button size="xs" variant="subtle" color="red" onClick={() => deactivate(e.id)}>Deactivate</Button>}
                  </Group></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      <Modal opened={modalOpen} onClose={() => { setModalOpen(false); setEditingEmployee(null); }} title={editingEmployee ? 'Edit employee' : 'Add employee'} radius="md">
        <Stack gap="sm">
          <TextInput label="Email" required type="email" value={form.email} onChange={set('email')} readOnly={Boolean(editingEmployee?.keycloakUserId)} description={editingEmployee?.keycloakUserId ? 'Email is locked because this profile is linked to a sign-in account.' : undefined} />
          <Group wrap="wrap" grow gap="xs">
            <TextInput label="First name" required value={form.firstName} onChange={set('firstName')} />
            <TextInput label="Last name" required value={form.lastName} onChange={set('lastName')} />
          </Group>
          <TextInput label="Phone" value={form.phone} onChange={set('phone')} />
          <Select label="Role" data={ROLES} value={form.role} onChange={(v) => setForm((f) => ({ ...f, role: v ?? 'employee' }))} />
          {form.role === 'partner' && <><Select label="Partner company" required searchable data={companies.filter(c => c.isActive && c.isPartnerEnabled).map(c => ({ value: c.id, label: c.name }))} value={form.partnerCompanyId || null} onChange={value => setForm(current => ({ ...current, partnerCompanyId: value ?? '' }))} description="Only companies with partner access enabled are available." />{companyError && <Text role="alert" c="red">{companyError}</Text>}</>}
          {editingEmployee && <>
            <TagsInput label="Skills" value={profile.skills} onChange={skills => setProfile(current => ({ ...current, skills }))} />
            <TagsInput label="Service areas" value={profile.serviceAreas} onChange={serviceAreas => setProfile(current => ({ ...current, serviceAreas }))} />
            <TextInput label="Colour" placeholder="#1a2b3c" value={profile.colorHex} onChange={e => { const colorHex = e.currentTarget.value; setProfile(current => ({ ...current, colorHex })); }} />
            <NumberInput label="Pay rate" decimalScale={2} value={profile.payRate} onChange={payRate => setProfile(current => ({ ...current, payRate: typeof payRate === 'number' ? payRate : '' }))} />
            <Textarea label="Notes" autosize minRows={2} value={profile.notes} onChange={e => { const notes = e.currentTarget.value; setProfile(current => ({ ...current, notes })); }} />
            <Stack gap="xs">
              <Text size="sm" fw={500}>Certifications</Text>
              {profile.certifications.map((cert, index) => (
                <Group key={index} gap="xs" wrap="wrap" align="flex-end">
                  <TextInput style={{ flex: 1, minWidth: 140 }} aria-label={`Certification ${index + 1} name`} placeholder="Name" value={cert.name} onChange={e => { const name = e.currentTarget.value; setProfile(current => ({ ...current, certifications: current.certifications.map((c, i) => i === index ? { ...c, name } : c) })); }} />
                  <TextInput type="date" aria-label={`Certification ${index + 1} expiry`} value={cert.expiresAt} onChange={e => { const expiresAt = e.currentTarget.value; setProfile(current => ({ ...current, certifications: current.certifications.map((c, i) => i === index ? { ...c, expiresAt } : c) })); }} />
                  <Button size="xs" variant="subtle" color="red" onClick={() => setProfile(current => ({ ...current, certifications: current.certifications.filter((_, i) => i !== index) }))}>Remove</Button>
                </Group>
              ))}
              <Button size="xs" variant="light" onClick={() => setProfile(current => ({ ...current, certifications: [...current.certifications, { name: '', expiresAt: '' }] }))}>Add certification</Button>
            </Stack>
          </>}
          {editingEmployee && (
            <Stack gap="xs">
              <Text size="sm" fw={500}>Default hours</Text>
              {WEEKDAYS.map(day => (
                <Group key={day} gap="xs" wrap="nowrap" align="flex-end">
                  <Text size="sm" w={96}>{day}</Text>
                  <TextInput type="time" aria-label={`${day} start`} value={profile.hours[day]?.start ?? ''} onChange={e => { const start = e.currentTarget.value; setProfile(current => ({ ...current, hours: { ...current.hours, [day]: { start, end: current.hours[day]?.end ?? '' } } })); }} />
                  <TextInput type="time" aria-label={`${day} end`} value={profile.hours[day]?.end ?? ''} onChange={e => { const end = e.currentTarget.value; setProfile(current => ({ ...current, hours: { ...current.hours, [day]: { start: current.hours[day]?.start ?? '', end } } })); }} />
                </Group>
              ))}
            </Stack>
          )}
          {error && <Text role="alert" size="sm" c="red">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingEmployee ? 'Save changes' : 'Create employee'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
