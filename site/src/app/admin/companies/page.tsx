'use client';

import { Fragment, useEffect, useRef, useState } from 'react';
import {
  Title, Button, Table, Modal, TextInput, Textarea, Group, Stack, Text, Loader, Badge,
} from '@mantine/core';
import { Plus, ChevronDown, Pencil } from 'lucide-react';
import { adminCompanies, adminBranches, type Company, type Branch } from '@/lib/adminApi';
import { companyLifecycle, branchLifecycle } from '@/lib/reactivationApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

const emptyForm = { businessId: '', name: '', contactName: '', contactEmail: '', contactPhone: '', notes: '' };

export default function CompaniesAdminPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [status, setStatus] = useState<LifecycleStatus>('active');
  const [branchStatus, setBranchStatus] = useState<LifecycleStatus>('active');
  const [actionError, setActionError] = useState('');
  const [actionBusy, setActionBusy] = useState<string | null>(null);
  const [listError, setListError] = useState('');
  const loadGeneration = useRef(0);
  const branchGeneration = useRef(0);
  const [branchLoading, setBranchLoading] = useState<string | null>(null);
  const [branchLoadError, setBranchLoadError] = useState('');
  const visibleCompanies = companies.filter(company => inLifecycle(company, status));
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingCompany, setEditingCompany] = useState<Company | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const [expanded, setExpanded] = useState<string | null>(null);
  const [companyBranchesMap, setCompanyBranchesMap] = useState<Record<string, Branch[]>>({});
  const [branchModal, setBranchModal] = useState(false);
  const [editingBranch, setEditingBranch] = useState<Branch | null>(null);
  const [branchForm, setBranchForm] = useState({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '', contactEmail: '' });
  const [branchSaving, setBranchSaving] = useState(false);
  const [branchError, setBranchError] = useState('');

  const load = async () => {
    const version = ++loadGeneration.current;
    setLoading(true); setListError('');
    const records = await adminCompanies.listAll();
    if (version !== loadGeneration.current) return;
    if (records) setCompanies(records);
    else setListError('Companies could not be loaded completely. Refresh to retry.');
    setLoading(false);
  };

  useEffect(() => { void load(); return () => { loadGeneration.current++; branchGeneration.current++; }; }, []);

  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));
  const setB = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setBranchForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async () => {
    if (!form.businessId.trim() || !form.name.trim()) { setError('Business ID and name are required.'); return; }
    setSaving(true); setError('');
    try {
      const fields = {
        businessId: form.businessId.trim(), name: form.name.trim(),
        contactName: form.contactName.trim() || undefined, contactEmail: form.contactEmail.trim() || undefined,
        contactPhone: form.contactPhone.trim() || undefined, notes: form.notes.trim() || undefined,
      };
      if (editingCompany) await adminCompanies.update(editingCompany.id, { ...fields, isActive: editingCompany.isActive });
      else await adminCompanies.create(fields);
      setForm(emptyForm); setEditingCompany(null); setModalOpen(false); if (!editingCompany) setStatus('active'); await load();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not create company. Please retry.');
    } finally { setSaving(false); }
  };

  const openCompanyEdit = (company: Company) => {
    setEditingCompany(company);
    setForm({ businessId: company.businessId, name: company.name, contactName: company.contactName ?? '', contactEmail: company.contactEmail ?? '', contactPhone: company.contactPhone ?? '', notes: company.notes ?? '' });
    setError(''); setModalOpen(true);
  };

  const loadBranches = async (id: string) => {
    const version = ++branchGeneration.current;
    setBranchLoading(id); setBranchLoadError('');
    const d = await adminCompanies.get(id);
    if (version !== branchGeneration.current) return;
    if (d) setCompanyBranchesMap((m) => ({ ...m, [id]: d.branches }));
    else setBranchLoadError('Branches could not be loaded. Please retry.');
    setBranchLoading(null);
  };
  const toggleBranches = async (id: string) => {
    if (expanded === id) { branchGeneration.current++; setExpanded(null); setBranchLoading(null); return; }
    setExpanded(id); setBranchStatus('active');
    await loadBranches(id);
  };

  const openedCustomer = useRef<string | null>(null);
  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get('company');
    // listAll loads both active and inactive records; visibleCompanies applies the tab filter separately.
    const customer = companies.find(company => company.id === requested);
    if (!customer || openedCustomer.current === requested) return;
    openedCustomer.current = requested;
    setStatus(customer.isActive ? 'active' : 'inactive');
    setExpanded(customer.id); setBranchStatus('active'); void loadBranches(customer.id);
  }, [companies]);

  const submitBranch = async () => {
    if (!expanded || !branchForm.name.trim()) return;
    setBranchSaving(true); setBranchError('');
    try {
      const fields = {
        name: branchForm.name.trim(), street: branchForm.street.trim() || undefined,
        postalCode: branchForm.postalCode.trim() || undefined, city: branchForm.city.trim() || undefined,
        country: branchForm.country.trim() || undefined, contactPhone: branchForm.contactPhone.trim() || undefined, contactEmail: branchForm.contactEmail.trim() || undefined,
      };
      if (editingBranch) {
        const updated = await adminBranches.update(editingBranch.id, { ...fields, isActive: editingBranch.isActive });
        if (!updated) throw new Error('Could not update branch. Please retry.');
      } else await adminBranches.create(expanded, fields);
      setBranchForm({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '', contactEmail: '' });
      setEditingBranch(null);
      setBranchModal(false);
      if (!editingBranch) setBranchStatus('active');
      await loadBranches(expanded);
    } catch (failure) {
      setBranchError(failure instanceof Error ? failure.message : 'Could not create branch. Please retry.');
    } finally { setBranchSaving(false); }
  };

  const openBranchEdit = (branch: Branch) => {
    setEditingBranch(branch);
    setBranchForm({ name: branch.name, street: branch.street ?? '', postalCode: branch.postalCode ?? '', city: branch.city ?? '', country: branch.country ?? '', contactPhone: branch.contactPhone ?? '', contactEmail: branch.contactEmail ?? '' });
    setBranchError(''); setBranchModal(true);
  };

  const changeActivity = async (id: string, active: boolean, companyId?: string) => {
    setActionBusy(id); setActionError('');
    try {
      const api = companyId ? branchLifecycle : companyLifecycle;
      await (active ? api.deactivate(id) : api.reactivate(id));
      if (companyId) await loadBranches(companyId);
      else { setExpanded(null); await load(); }
    } catch (failure) {
      setActionError(failure instanceof Error ? failure.message : 'Could not change activity. Please retry.');
    } finally { setActionBusy(null); }
  };

  const deleteCompany = async (company: Company) => {
    if (!window.confirm(`Permanently delete inactive company “${company.name}”? Companies with branches or business history are retained.`)) return;
    setActionBusy(company.id); setActionError('');
    try { await adminCompanies.deleteInactive(company.id); setExpanded(null); await load(); }
    catch (failure) { setActionError(failure instanceof Error ? failure.message : 'Could not delete company. Please retry.'); }
    finally { setActionBusy(null); }
  };

  const deleteBranch = async (branch: Branch, companyId: string) => {
    if (!window.confirm(`Permanently delete inactive branch “${branch.name}”? Branches with shift or checklist history are retained.`)) return;
    setActionBusy(branch.id); setActionError('');
    try { await adminBranches.deleteInactive(branch.id); await loadBranches(companyId); }
    catch (failure) { setActionError(failure instanceof Error ? failure.message : 'Could not delete branch. Please retry.'); }
    finally { setActionBusy(null); }
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Companies</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => { setEditingCompany(null); setForm(emptyForm); setError(''); setModalOpen(true); }}>Add company</Button>
      </Group>

      {actionError && <Text c="red" role="alert" mb="sm">{actionError}</Text>}
      <LifecycleTabs value={status} onChange={next => { setStatus(next); setExpanded(null); branchGeneration.current++; setBranchLoading(null); setBranchLoadError(''); }} label="Company activity"
        activeCount={loading || listError ? undefined : companies.filter(c => c.isActive).length}
        inactiveCount={loading || listError ? undefined : companies.filter(c => !c.isActive).length}>
      {loading ? (
        <Loader />
      ) : listError ? (
        <Stack><Text role="alert" c="red">{listError}</Text><Button variant="light" onClick={() => void load()}>Refresh companies</Button></Stack>
      ) : visibleCompanies.length === 0 ? (
        <Text c="dimmed">{companies.length === 0 ? 'No companies yet. Add one to get started.' : `No ${status} companies.`}</Text>
      ) : (
        <Table.ScrollContainer minWidth={550}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Business ID</Table.Th>
                <Table.Th>Contact</Table.Th>
                <Table.Th w={120}>Branches</Table.Th>
                <Table.Th>Activity</Table.Th><Table.Th w={110}>Actions</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleCompanies.map((c) => {
                const currentBranches = companyBranchesMap[c.id] ?? [];
                const visibleBranches = currentBranches.filter(branch => inLifecycle(branch, branchStatus));
                const isExpanded = expanded === c.id;
                return (
                  <Fragment key={c.id}>
                    <Table.Tr>
                      <Table.Td fw={500}>{c.name}</Table.Td>
                      <Table.Td>{c.businessId}</Table.Td>
                      <Table.Td>{c.contactName || '—'}</Table.Td>
                      <Table.Td>
                        <Button variant="subtle" size="xs" onClick={() => toggleBranches(c.id)}>
                          {isExpanded ? currentBranches.length : 'Branches'} <ChevronDown size={14} />
                        </Button>
                      </Table.Td>
                      <Table.Td><Button size="xs" variant="light" color={c.isActive ? 'red' : undefined}
                        loading={actionBusy === c.id} disabled={actionBusy !== null && actionBusy !== c.id}
                        onClick={() => void changeActivity(c.id, c.isActive)}>{c.isActive ? 'Deactivate' : 'Reactivate'}</Button></Table.Td>
                      <Table.Td><Group gap={4} wrap="nowrap"><Button size="xs" variant="subtle" leftSection={<Pencil size={13} />} onClick={() => openCompanyEdit(c)}>Edit</Button>{!c.isActive && <Button size="xs" color="red" variant="subtle" loading={actionBusy === c.id} disabled={!!actionBusy} onClick={() => void deleteCompany(c)}>Delete</Button>}</Group></Table.Td>
                    </Table.Tr>
                    {isExpanded && (
                      <Table.Tr>
                        <Table.Td colSpan={6} p="sm">
                          <Group justify="space-between" align="center" mb="xs" wrap="wrap" gap="xs">
                            <Text size="sm" fw={600}>Branches</Text>
                            <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={() => { setEditingBranch(null); setBranchForm({ name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '', contactEmail: '' }); setBranchError(''); setBranchModal(true); }}>Add branch</Button>
                          </Group>
                          <LifecycleTabs value={branchStatus} onChange={setBranchStatus} label="Branch activity"
                            activeCount={branchLoading === c.id || branchLoadError ? undefined : currentBranches.filter(b => b.isActive).length}
                            inactiveCount={branchLoading === c.id || branchLoadError ? undefined : currentBranches.filter(b => !b.isActive).length}>
                          {branchLoading === c.id ? <Loader size="sm" /> : branchLoadError ? (
                            <Stack><Text role="alert" c="red">{branchLoadError}</Text><Button variant="light" onClick={() => void loadBranches(c.id)}>Refresh branches</Button></Stack>
                          ) : visibleBranches.length === 0 ? (
                            <Text size="sm" c="dimmed">{currentBranches.length === 0 ? 'No branches yet.' : `No ${branchStatus} branches.`}</Text>
                          ) : (
                            <Stack gap={4}>
                              {visibleBranches.map((b) => (
                                <Group key={b.id} justify="space-between" wrap="wrap" gap="xs">
                                  <Text size="sm">{b.name}{b.city ? ` · ${b.city}` : ''}</Text>
                                  <Group gap="xs">{!b.isActive && <Badge color="gray" size="xs">Inactive</Badge>}
                                    <Button size="xs" variant="subtle" leftSection={<Pencil size={13} />} onClick={() => openBranchEdit(b)}>Edit</Button>
                                    <Button size="xs" variant="light" color={b.isActive ? 'red' : undefined}
                                      loading={actionBusy === b.id} disabled={actionBusy !== null && actionBusy !== b.id}
                                      onClick={() => void changeActivity(b.id, b.isActive, c.id)}>{b.isActive ? 'Deactivate' : 'Reactivate'}</Button>
                                    {!b.isActive && <Button size="xs" color="red" variant="subtle" loading={actionBusy === b.id} disabled={!!actionBusy} onClick={() => void deleteBranch(b, c.id)}>Delete</Button>}
                                  </Group>
                                </Group>
                              ))}
                            </Stack>
                          )}
                          </LifecycleTabs>
                        </Table.Td>
                      </Table.Tr>
                    )}
                  </Fragment>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      <Modal opened={modalOpen} onClose={() => { setModalOpen(false); setEditingCompany(null); }} title={editingCompany ? 'Edit company' : 'Add company'} radius="md">
        <Stack gap="sm">
          <TextInput label="Business ID" required value={form.businessId} onChange={set('businessId')} />
          <TextInput label="Name" required value={form.name} onChange={set('name')} />
          <TextInput label="Contact name" value={form.contactName} onChange={set('contactName')} />
          <TextInput label="Contact email" value={form.contactEmail} onChange={set('contactEmail')} />
          <TextInput label="Contact phone" value={form.contactPhone} onChange={set('contactPhone')} />
          <Textarea label="Notes" value={form.notes} onChange={set('notes')} />
          {error && <Text size="sm" c="red" role="alert">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button loading={saving} onClick={submit}>{editingCompany ? 'Save changes' : 'Create company'}</Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={branchModal} onClose={() => { setBranchModal(false); setEditingBranch(null); }} title={editingBranch ? 'Edit branch' : 'Add branch'} radius="md">
        <Stack gap="sm">
          <TextInput label="Name" required value={branchForm.name} onChange={setB('name')} />
          {branchError && <Text c="red" role="alert">{branchError}</Text>}
          <TextInput label="Street" value={branchForm.street} onChange={setB('street')} />
          <TextInput label="Postal code" value={branchForm.postalCode} onChange={setB('postalCode')} />
          <TextInput label="City" value={branchForm.city} onChange={setB('city')} />
          <TextInput label="Country" value={branchForm.country} onChange={setB('country')} />
          <TextInput label="Contact email" type="email" value={branchForm.contactEmail} onChange={setB('contactEmail')} />
          <TextInput label="Phone" value={branchForm.contactPhone} onChange={setB('contactPhone')} />
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setBranchModal(false)}>Cancel</Button>
            <Button loading={branchSaving} onClick={submitBranch}>{editingBranch ? 'Save changes' : 'Add branch'}</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
