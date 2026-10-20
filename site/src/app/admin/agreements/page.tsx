'use client';

import { useCallback, useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge, ActionIcon, Tooltip, CopyButton, Paper } from '@mantine/core';
import { Plus, Trash2, Copy, Mail, Check, Eye } from 'lucide-react';
import { adminAgreements, adminCompanies, authorizedFetch, invalidateAdminCache, type AgreementListItem, type AgreementDetail, type Company, type LegacyAgreementRepairPreview } from '@/lib/adminApi';
import { AgreementEmailCopyButton } from '@/components/AgreementEmailCopyButton';
import { agreementEmail } from '@/lib/agreementEmail';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

export default function AgreementsPage() {
  const [items, setItems] = useState<AgreementListItem[] | null>(null);
  const [lifecycle, setLifecycle] = useState<LifecycleStatus>('active');
  const visibleItems = items?.filter(item => inLifecycle(item, lifecycle)) ?? [];
  const [companies, setCompanies] = useState<Company[]>([]);
  const [lifecycleError, setLifecycleError] = useState('');
  const [lifecycleBusy, setLifecycleBusy] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState('');
  const [companyId, setCompanyId] = useState<string | null>(null);
  const [pdf, setPdf] = useState<File | null>(null);
  const [signers, setSigners] = useState<{ name: string; email: string }[]>([{ name: '', email: '' }]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const [detailModalOpen, setDetailModalOpen] = useState(false);
  const [selectedAgreement, setSelectedAgreement] = useState<AgreementDetail | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  const [createdAgreement, setCreatedAgreement] = useState<AgreementDetail | null>(null);
  const [repairPreview, setRepairPreview] = useState<LegacyAgreementRepairPreview | null>(null);
  const [repairLoading, setRepairLoading] = useState(true);
  const [repairSelections, setRepairSelections] = useState<Record<string, string | null>>({});
  const [repairBusy, setRepairBusy] = useState<string | null>(null);
  const [repairError, setRepairError] = useState('');

  const load = useCallback(async () => { setItems(await adminAgreements.list()); }, []);
  const loadRepairPreview = useCallback(async () => {
    setRepairLoading(true);
    try {
      const preview = await adminAgreements.legacyRepairPreview();
      setRepairPreview(preview);
      if (!preview) setRepairError('Could not load legacy agreement links. Refresh and try again.');
      else setRepairError('');
      setRepairSelections(Object.fromEntries((preview?.agreements ?? []).map(item => [item.agreementId, null])));
    } catch {
      setRepairPreview(null);
      setRepairError('Could not load legacy agreement links. Refresh and try again.');
    } finally { setRepairLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); }); }, []);
  useEffect(() => { loadRepairPreview(); }, [loadRepairPreview]);

  const repairCompany = async (agreementId: string) => {
    const selectedCompanyId = repairSelections[agreementId];
    const candidate = repairPreview?.agreements.find(item => item.agreementId === agreementId);
    if (!selectedCompanyId || !candidate) return;
    setRepairBusy(agreementId); setRepairError('');
    const result = await adminAgreements.repairLegacyCompany(agreementId, selectedCompanyId, candidate.expectedVersion);
    if (!result.ok) setRepairError(result.error || 'Could not repair this agreement.');
    else await loadRepairPreview();
    setRepairBusy(null);
  };

  const changeActivity = async (agreement: AgreementListItem) => {
    setLifecycleBusy(agreement.id); setLifecycleError('');
    try {
      const response = await authorizedFetch(`/api/v1/admin/agreements/${agreement.id}/${agreement.isActive ? 'deactivate' : 'activate'}`, { method: 'POST' });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({}));
        throw new Error(problem.detail || problem.title || 'Could not update agreement activity. Please retry.');
      }
      invalidateAdminCache(); await load();
    } catch (failure) { setLifecycleError(failure instanceof Error ? failure.message : 'Could not update agreement activity. Please retry.'); }
    finally { setLifecycleBusy(null); }
  };

  const deleteAgreement = async (agreement: AgreementListItem) => {
    const signed = agreement.hasSignedEvidence || agreement.signedCount > 0 || agreement.status === 'PartiallySigned' || agreement.status === 'Completed';
    const message = signed
      ? `Permanently delete signed agreement “${agreement.title}”? Its signing and download links will stop working. This cannot be undone.`
      : `Permanently delete inactive agreement “${agreement.title}”? This cannot be undone.`;
    if (!window.confirm(message)) return;
    setLifecycleBusy(agreement.id); setLifecycleError('');
    try { await adminAgreements.deleteInactive(agreement.id, signed); await load(); }
    catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Could not delete agreement. Please retry.';
      if (!signed && message.toLowerCase().includes('signed evidence')) {
        invalidateAdminCache(); await load();
      }
      setLifecycleError(message);
    }
    finally { setLifecycleBusy(null); }
  };

  const create = async () => {
    const validSigners = signers.filter((s) => s.name.trim() && s.email.trim());
    if (!pdf || !companyId || !title.trim()) { setError('Company, title and PDF are required.'); return; }
    if (validSigners.length === 0) { setError('At least one signer with name and email is required.'); return; }
    setSubmitting(true); setError('');
    const res = await adminAgreements.create(title.trim(), companyId, pdf, validSigners);
    setSubmitting(false);
    if (!res.ok) { setError(res.error); return; }
    setLifecycle('active');
    setOpen(false); setTitle(''); setCompanyId(null); setPdf(null); setSigners([{ name: '', email: '' }]);
    setCreatedAgreement(res.data);
    load();
  };

  const handleDownload = (id: string) => {
    adminAgreements.downloadDocument(id);
  };

  const openDetail = async (id: string) => {
    setLoadingDetail(true);
    setDetailModalOpen(true);
    const detail = await adminAgreements.get(id);
    setSelectedAgreement(detail);
    setLoadingDetail(false);
  };

  const getSignLink = (token: string) => {
    const origin = typeof window !== 'undefined' ? window.location.origin : '';
    return `${origin}/fi/sign?token=${encodeURIComponent(token)}`;
  };

  const renderSignerActions = (agreementTitle: string, signerName: string, signerEmail: string, token: string) => {
    const link = getSignLink(token);
    const body = agreementEmail(agreementTitle, signerName, link).text;
    const subject = encodeURIComponent(`Sopimus allekirjoitettavaksi: ${agreementTitle}`);
    const mailto = `mailto:${signerEmail}?subject=${subject}&body=${encodeURIComponent(body)}`;

    return (
      <Group gap={6} wrap="wrap">
        <CopyButton value={link} timeout={2000}>
          {({ copied, copy }) => (
            <Tooltip label={copied ? 'Copied link!' : 'Copy signing link'} withArrow>
              <Button size="xs" variant="light" color={copied ? 'teal' : 'blue'} leftSection={copied ? <Check size={14} /> : <Copy size={14} />} onClick={copy}>
                {copied ? 'Copied link' : 'Copy link'}
              </Button>
            </Tooltip>
          )}
        </CopyButton>

        <AgreementEmailCopyButton title={agreementTitle} signerName={signerName} link={link} />

        <Tooltip label="Open mail app (plain text); use Copy email for formatting" withArrow>
          <ActionIcon component="a" aria-label="Open mail app (plain text)" href={mailto} variant="subtle" color="blue" size="sm">
            <Mail size={14} />
          </ActionIcon>
        </Tooltip>
      </Group>
    );
  };

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Title order={2}>Agreements</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setOpen(true)}>New agreement</Button>
      </Group>

      {lifecycleError && <Text role="alert" c="red">{lifecycleError}</Text>}
      {(repairLoading || repairError || (repairPreview && repairPreview.agreements.length > 0)) && <Paper withBorder p="md" mb="md" radius="md">
        <Stack gap="sm">
          <div><Text fw={600}>Legacy agreements needing a company link</Text><Text size="sm" c="dimmed">Review each unsigned agreement and select its correct company. Signed agreements are excluded.</Text></div>
          {repairError && <Text role="alert" c="red">{repairError}</Text>}
          {repairLoading && <Loader size="sm" />}
          {!repairLoading && repairPreview?.agreements.length === 0 && <Text size="sm" c="dimmed">No eligible legacy agreements need repair.</Text>}
          {!repairLoading && repairPreview && repairPreview.agreements.length > 0 && <>
          <Table.ScrollContainer minWidth={650}><Table withTableBorder>
            <Table.Thead><Table.Tr><Table.Th>Agreement</Table.Th><Table.Th>Status</Table.Th><Table.Th>Company</Table.Th><Table.Th>Action</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>{repairPreview.agreements.map(item => <Table.Tr key={item.agreementId}>
              <Table.Td>{item.title}</Table.Td><Table.Td>{item.status}</Table.Td>
              <Table.Td><Select aria-label={`Company for ${item.title}`} placeholder="Choose company" searchable data={repairPreview.companies.map(company => ({ value: company.id, label: `${company.name}${company.isActive ? '' : ' (inactive)'}` }))} value={repairSelections[item.agreementId] ?? null} onChange={value => setRepairSelections(current => ({ ...current, [item.agreementId]: value }))} /></Table.Td>
              <Table.Td><Button size="xs" loading={repairBusy === item.agreementId} disabled={!repairSelections[item.agreementId] || !!repairBusy} onClick={() => repairCompany(item.agreementId)}>Apply reviewed link</Button></Table.Td>
            </Table.Tr>)}</Table.Tbody>
          </Table></Table.ScrollContainer>
          </>}
        </Stack>
      </Paper>}
      <LifecycleTabs value={lifecycle} onChange={setLifecycle} label="Agreement activity"
        activeCount={items?.filter(item => item.isActive).length} inactiveCount={items?.filter(item => !item.isActive).length}>
      {items === null ? <Loader /> : items.length === 0 ? (
        <Text c="dimmed">No agreements yet.</Text>
      ) : visibleItems.length === 0 ? (
        <Text c="dimmed">No {lifecycle} agreements. Check the {lifecycle === 'active' ? 'Inactive' : 'Active'} tab.</Text>
      ) : (
        <Table.ScrollContainer minWidth={600}>
          <Table striped highlightOnHover withTableBorder>
            <Table.Thead>
              <Table.Tr><Table.Th>Title</Table.Th><Table.Th>Code</Table.Th><Table.Th>Signers</Table.Th><Table.Th>Status</Table.Th><Table.Th w={180}>Actions</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleItems.map((a) => (
                <Table.Tr key={a.id}>
                  <Table.Td fw={500}>{a.title}</Table.Td>
                  <Table.Td>{a.code}</Table.Td>
                  <Table.Td>{a.signedCount} / {a.signerCount}</Table.Td>
                  <Table.Td><Badge color={a.isActive ? 'green' : 'gray'}>{a.status}</Badge></Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="wrap">
                      <Button size="xs" variant="light" leftSection={<Eye size={14} />} onClick={() => openDetail(a.id)}>Signers</Button>
                      <Button size="xs" variant="outline" onClick={() => handleDownload(a.id)}>PDF</Button>
                      <Button size="xs" loading={lifecycleBusy === a.id} disabled={!!lifecycleBusy || (!a.isActive && a.status === 'Cancelled')} onClick={() => changeActivity(a)}>{a.isActive ? 'Deactivate' : 'Reactivate'}</Button>
                      {!a.isActive && <Button size="xs" color="red" variant="subtle" loading={lifecycleBusy === a.id} disabled={!!lifecycleBusy} onClick={() => deleteAgreement(a)}>Delete</Button>}
                      {!a.isActive && a.status === 'Cancelled' && <Text size="xs">Cancelled agreements cannot be reactivated.</Text>}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      </LifecycleTabs>

      {/* New Agreement Modal */}
      <Modal opened={open} onClose={() => setOpen(false)} title="New agreement" size="lg" radius="md">
        <Stack gap="sm">
          <Select label="Company" required data={companies.map((c) => ({ value: c.id, label: c.name }))} value={companyId} onChange={setCompanyId} searchable />
          <TextInput label="Title" required value={title} onChange={(e) => setTitle(e.target.value)} />
          <div>
            <Text size="sm" fw={500} mb={4}>PDF Document</Text>
            <input type="file" accept=".pdf" onChange={(e) => setPdf(e.target.files?.[0] ?? null)} style={{ width: '100%' }} />
          </div>
          <Stack gap={8}>
            <Text size="sm" fw={500}>Signers</Text>
            {signers.map((s, i) => (
              <Group key={i} gap={8} wrap="nowrap" align="center">
                <TextInput placeholder="Name" value={s.name} onChange={(e) => { const n = [...signers]; n[i].name = e.target.value; setSigners(n); }} style={{ flex: 1 }} />
                <TextInput placeholder="Email" value={s.email} onChange={(e) => { const n = [...signers]; n[i].email = e.target.value; setSigners(n); }} style={{ flex: 1 }} />
                <ActionIcon variant="subtle" color="red" onClick={() => setSigners(signers.filter((_, j) => j !== i))}><Trash2 size={16} /></ActionIcon>
              </Group>
            ))}
            <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={() => setSigners([...signers, { name: '', email: '' }])}>Add signer</Button>
          </Stack>
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setOpen(false)}>Cancel</Button>
            <Button loading={submitting} onClick={create}>Create</Button>
          </Group>
        </Stack>
      </Modal>

      {/* Agreement Detail / Signers Modal */}
      <Modal opened={detailModalOpen} onClose={() => { setDetailModalOpen(false); setSelectedAgreement(null); }} title={selectedAgreement?.title ?? 'Agreement Signers'} size="lg" radius="md">
        {loadingDetail || !selectedAgreement ? (
          <Loader />
        ) : (
          <Stack gap="md">
            <Group justify="space-between" wrap="wrap">
              <Text size="sm">Code: <b>{selectedAgreement.code}</b></Text>
              <Badge color={selectedAgreement.isActive ? 'green' : 'gray'}>{selectedAgreement.status}</Badge>
            </Group>
            <Text size="sm" fw={500}>Signers ({selectedAgreement.signedCount}/{selectedAgreement.signerCount} signed):</Text>
            <Stack gap="xs">
              {selectedAgreement.signers.map((s) => (
                <Stack key={s.id} p="xs" style={{ border: '1px solid var(--mantine-color-gray-3)', borderRadius: 8 }}>
                  <Group justify="space-between" wrap="wrap" gap="xs">
                    <div>
                      <Text size="sm" fw={500}>{s.name}</Text>
                      <Text size="xs" c="dimmed">{s.email}</Text>
                    </div>
                    <Badge size="sm" color={s.status === 'Signed' ? 'green' : 'yellow'}>{s.status}</Badge>
                  </Group>
                  {renderSignerActions(selectedAgreement.title, s.name, s.email, s.token)}
                </Stack>
              ))}
            </Stack>
          </Stack>
        )}
      </Modal>

      {/* Post-Creation Signer Links Modal */}
      <Modal opened={createdAgreement !== null} onClose={() => setCreatedAgreement(null)} title="Agreement Created Successfully" size="lg" radius="md">
        {createdAgreement && (
          <Stack gap="md">
            <Text size="sm">Agreement <b>{createdAgreement.title}</b> has been created. Use the buttons below to copy signing links or email messages for each signer:</Text>
            <Stack gap="xs">
              {createdAgreement.signers.map((s) => (
                <Stack key={s.id} p="xs" style={{ border: '1px solid var(--mantine-color-gray-3)', borderRadius: 8 }}>
                  <div>
                    <Text size="sm" fw={500}>{s.name}</Text>
                    <Text size="xs" c="dimmed">{s.email}</Text>
                  </div>
                  {renderSignerActions(createdAgreement.title, s.name, s.email, s.token)}
                </Stack>
              ))}
            </Stack>
            <Group justify="flex-end">
              <Button onClick={() => setCreatedAgreement(null)}>Close</Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </>
  );
}
