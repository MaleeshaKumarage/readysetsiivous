'use client';

import { useEffect, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge, ActionIcon,
} from '@mantine/core';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { adminLeads, type Lead, type LeadContactPerson } from '@/lib/leadsApi';
import { leadLifecycle } from '@/lib/reactivationApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

const emptyContact = (): LeadContactPerson => ({ name: '', email: '', phone: '', role: '' });

const emptyForm = {
  name: '',
  businessId: '',
  mainContactName: '',
  mainContactEmail: '',
  mainContactPhone: '',
  emails: [''] as string[],
  contactPersons: [emptyContact()] as LeadContactPerson[],
  notes: '',
};

export default function LeadsAdminPage() {
  const [leads, setLeads] = useState<Lead[]>([]);
  const [status, setStatus] = useState<LifecycleStatus>('active');
  const [actionError, setActionError] = useState('');
  const [actionBusy, setActionBusy] = useState<string | null>(null);
  const [listError, setListError] = useState('');
  const loadGeneration = useRef(0);
  const visibleLeads = leads.filter(lead => inLifecycle(lead, status));
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingLead, setEditingLead] = useState<Lead | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = async () => {
    const version = ++loadGeneration.current;
    setLoading(true); setListError('');
    const records = await adminLeads.listAll();
    if (version !== loadGeneration.current) return;
    if (records) setLeads(records);
    else setListError('Leads could not be loaded completely. Refresh to retry.');
    setLoading(false);
  };

  useEffect(() => { void load(); return () => { loadGeneration.current++; }; }, []);

  const setField = (k: keyof typeof emptyForm) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  const setEmail = (index: number, value: string) =>
    setForm((f) => ({ ...f, emails: f.emails.map((em, i) => (i === index ? value : em)) }));

  const addEmail = () => setForm((f) => ({ ...f, emails: [...f.emails, ''] }));
  const removeEmail = (index: number) =>
    setForm((f) => ({ ...f, emails: f.emails.length <= 1 ? f.emails : f.emails.filter((_, i) => i !== index) }));

  const setPerson = (index: number, key: keyof LeadContactPerson, value: string) =>
    setForm((f) => ({
      ...f,
      contactPersons: f.contactPersons.map((p, i) => (i === index ? { ...p, [key]: value } : p)),
    }));

  const addPerson = () => setForm((f) => ({ ...f, contactPersons: [...f.contactPersons, emptyContact()] }));
  const removePerson = (index: number) =>
    setForm((f) => ({
      ...f,
      contactPersons: f.contactPersons.length <= 1 ? f.contactPersons : f.contactPersons.filter((_, i) => i !== index),
    }));

  const submit = async () => {
    if (!form.name.trim()) { setError('Name is required.'); return; }
    setSaving(true); setError('');
    try {
      // Send empty strings (not omitted) so a cleared field wipes the stored value.
      // Omitted/null on the API means "leave unchanged".
      const fields = {
        name: form.name.trim(),
        businessId: form.businessId.trim(),
        mainContactName: form.mainContactName.trim(),
        mainContactEmail: form.mainContactEmail.trim(),
        mainContactPhone: form.mainContactPhone.trim(),
        emails: form.emails.map((e) => e.trim()).filter(Boolean),
        contactPersons: form.contactPersons
          .map((p) => ({
            name: (p.name ?? '').trim(),
            email: (p.email ?? '').trim(),
            phone: (p.phone ?? '').trim(),
            role: (p.role ?? '').trim(),
          }))
          .filter((p) => p.name),
        notes: form.notes.trim(),
      };
      if (editingLead) await adminLeads.update(editingLead.id, { ...fields, isActive: editingLead.isActive });
      else await adminLeads.create(fields);
      setForm(emptyForm); setEditingLead(null); setModalOpen(false); if (!editingLead) setStatus('active'); await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save lead. Please retry.');
    } finally { setSaving(false); }
  };

  const openEdit = (lead: Lead) => {
    setEditingLead(lead);
    setForm({
      name: lead.name,
      businessId: lead.businessId ?? '',
      mainContactName: lead.mainContactName ?? '',
      mainContactEmail: lead.mainContactEmail ?? '',
      mainContactPhone: lead.mainContactPhone ?? '',
      emails: lead.emails?.length ? [...lead.emails] : [''],
      contactPersons: lead.contactPersons?.length
        ? lead.contactPersons.map((p) => ({ name: p.name, email: p.email ?? '', phone: p.phone ?? '', role: p.role ?? '' }))
        : [emptyContact()],
      notes: lead.notes ?? '',
    });
    setError(''); setModalOpen(true);
  };

  const changeActivity = async (id: string, active: boolean) => {
    setActionBusy(id); setActionError('');
    try {
      await (active ? leadLifecycle.deactivate(id) : leadLifecycle.reactivate(id));
      await load();
    } catch (failure) {
      setActionError(failure instanceof Error ? failure.message : 'Could not change activity. Please retry.');
    } finally { setActionBusy(null); }
  };

  const deleteLead = async (lead: Lead) => {
    if (!window.confirm(`Permanently delete inactive lead “${lead.name}”?`)) return;
    setActionBusy(lead.id); setActionError('');
    try { await adminLeads.deleteInactive(lead.id); await load(); }
    catch (failure) { setActionError(failure instanceof Error ? failure.message : 'Could not delete lead. Please retry.'); }
    finally { setActionBusy(null); }
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Leads</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => { setEditingLead(null); setForm(emptyForm); setError(''); setModalOpen(true); }}>
          Add lead
        </Button>
      </Group>

      {actionError && <Text c="red" role="alert" mb="sm">{actionError}</Text>}
      <LifecycleTabs value={status} onChange={setStatus} label="Lead activity"
        activeCount={loading || listError ? undefined : leads.filter(l => l.isActive).length}
        inactiveCount={loading || listError ? undefined : leads.filter(l => !l.isActive).length}>
      {loading ? (
        <Loader />
      ) : listError ? (
        <Stack><Text role="alert" c="red">{listError}</Text><Button variant="light" onClick={() => void load()}>Refresh leads</Button></Stack>
      ) : visibleLeads.length === 0 ? (
        <Text c="dimmed">{leads.length === 0 ? 'No leads yet. Add one to get started.' : `No ${status} leads.`}</Text>
      ) : (
        <Table.ScrollContainer minWidth={600}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Main contact</Table.Th>
                <Table.Th>Emails</Table.Th>
                <Table.Th>People</Table.Th>
                <Table.Th>Activity</Table.Th>
                <Table.Th w={110}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleLeads.map((lead) => (
                <Table.Tr key={lead.id}>
                  <Table.Td fw={500}>{lead.name}{lead.businessId ? <Text span size="xs" c="dimmed"> · {lead.businessId}</Text> : null}</Table.Td>
                  <Table.Td>
                    {lead.mainContactName || '—'}
                    {lead.mainContactEmail ? <Text size="xs" c="dimmed">{lead.mainContactEmail}</Text> : null}
                  </Table.Td>
                  <Table.Td>
                    {(lead.emails?.length ?? 0) === 0 ? '—' : lead.emails!.map((e) => (
                      <Text key={e} size="sm">{e}</Text>
                    ))}
                  </Table.Td>
                  <Table.Td>
                    {(lead.contactPersons?.length ?? 0) === 0 ? '—' : (
                      <Group gap={4}>
                        <Badge size="sm" variant="light">{lead.contactPersons!.length}</Badge>
                        <Text size="xs" c="dimmed" lineClamp={1}>
                          {lead.contactPersons!.map((p) => p.name).join(', ')}
                        </Text>
                      </Group>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Button size="xs" variant="light" color={lead.isActive ? 'red' : undefined}
                      loading={actionBusy === lead.id} disabled={actionBusy !== null && actionBusy !== lead.id}
                      onClick={() => void changeActivity(lead.id, lead.isActive)}>
                      {lead.isActive ? 'Deactivate' : 'Reactivate'}
                    </Button>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <Button size="xs" variant="subtle" leftSection={<Pencil size={13} />} onClick={() => openEdit(lead)}>Edit</Button>
                      {!lead.isActive && (
                        <Button size="xs" color="red" variant="subtle" loading={actionBusy === lead.id} disabled={!!actionBusy}
                          onClick={() => void deleteLead(lead)}>Delete</Button>
                      )}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      <Modal opened={modalOpen} onClose={() => { setModalOpen(false); setEditingLead(null); }}
        title={editingLead ? 'Edit lead' : 'Add lead'} radius="md" size="lg">
        <Stack gap="sm">
          <TextInput label="Name" required value={form.name} onChange={setField('name')} />
          <TextInput label="Business ID" value={form.businessId} onChange={setField('businessId')} />
          <Text fw={600} size="sm" mt="xs">Main contact</Text>
          <TextInput label="Name" value={form.mainContactName} onChange={setField('mainContactName')} />
          <TextInput label="Email" type="email" value={form.mainContactEmail} onChange={setField('mainContactEmail')} />
          <TextInput label="Phone" value={form.mainContactPhone} onChange={setField('mainContactPhone')} />

          <Group justify="space-between" align="center" mt="xs">
            <Text fw={600} size="sm">Additional emails</Text>
            <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={addEmail}>Add email</Button>
          </Group>
          {form.emails.map((email, i) => (
            <Group key={i} gap="xs" wrap="nowrap">
              <TextInput style={{ flex: 1 }} type="email" placeholder="email@example.com" value={email}
                onChange={(e) => setEmail(i, e.currentTarget.value)} />
              <ActionIcon variant="subtle" color="red" aria-label={`Remove email ${i + 1}`}
                onClick={() => removeEmail(i)} disabled={form.emails.length <= 1}>
                <Trash2 size={16} />
              </ActionIcon>
            </Group>
          ))}

          <Group justify="space-between" align="center" mt="xs">
            <Text fw={600} size="sm">Contact persons</Text>
            <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={addPerson}>Add person</Button>
          </Group>
          {form.contactPersons.map((person, i) => (
            <Stack key={i} gap={6} p="xs" style={{ border: '1px solid var(--mantine-color-dark-4)', borderRadius: 8 }}>
              <Group justify="space-between">
                <Text size="xs" c="dimmed">Person {i + 1}</Text>
                <ActionIcon variant="subtle" color="red" size="sm" aria-label={`Remove contact person ${i + 1}`}
                  onClick={() => removePerson(i)} disabled={form.contactPersons.length <= 1}>
                  <Trash2 size={14} />
                </ActionIcon>
              </Group>
              <TextInput label="Name" value={person.name} onChange={(e) => setPerson(i, 'name', e.currentTarget.value)} />
              <Group grow>
                <TextInput label="Email" type="email" value={person.email ?? ''}
                  onChange={(e) => setPerson(i, 'email', e.currentTarget.value)} />
                <TextInput label="Phone" value={person.phone ?? ''}
                  onChange={(e) => setPerson(i, 'phone', e.currentTarget.value)} />
              </Group>
              <TextInput label="Role / title" value={person.role ?? ''}
                onChange={(e) => setPerson(i, 'role', e.currentTarget.value)} />
            </Stack>
          ))}

          <Textarea label="Notes" value={form.notes} onChange={setField('notes')} />
          {error && <Text size="sm" c="red" role="alert">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingLead ? 'Save changes' : 'Create lead'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
