'use client';

import { useCallback, useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge, ActionIcon, Tooltip, CopyButton } from '@mantine/core';
import { Plus, Trash2, Copy, Mail, Check, Eye } from 'lucide-react';
import { adminAgreements, adminCompanies, type AgreementListItem, type AgreementDetail, type Company } from '@/lib/adminApi';

export default function AgreementsPage() {
  const [items, setItems] = useState<AgreementListItem[] | null>(null);
  const [companies, setCompanies] = useState<Company[]>([]);
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

  const load = useCallback(async () => { setItems(await adminAgreements.list()); }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); }); }, []);

  const create = async () => {
    const validSigners = signers.filter((s) => s.name.trim() && s.email.trim());
    if (!pdf || !companyId || !title.trim()) { setError('Company, title and PDF are required.'); return; }
    if (validSigners.length === 0) { setError('At least one signer with name and email is required.'); return; }
    setSubmitting(true); setError('');
    const res = await adminAgreements.create(title.trim(), companyId, pdf, validSigners);
    setSubmitting(false);
    if (!res.ok) { setError(res.error); return; }
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
    return `${origin}/fi/sign?token=${token}`;
  };

  const getEmailBody = (title: string, signerName: string, token: string) => {
    const link = getSignLink(token);
    return `Hei ${signerName},\n\nSopimuksen "${title}" allekirjoituslinkki:\n${link}\n\nLainvoimainen allekirjoitus suoritetaan verkkosivullamme.\n\nReadySetSiivous`;
  };

  const renderSignerActions = (agreementTitle: string, signerName: string, signerEmail: string, token: string) => {
    const link = getSignLink(token);
    const body = getEmailBody(agreementTitle, signerName, token);
    const subject = encodeURIComponent(`Sopimus allekirjoitettavaksi: ${agreementTitle}`);
    const mailto = `mailto:${signerEmail}?subject=${subject}&body=${encodeURIComponent(body)}`;

    return (
      <Group gap={6}>
        <CopyButton value={link} timeout={2000}>
          {({ copied, copy }) => (
            <Tooltip label={copied ? 'Copied link!' : 'Copy signing link'} withArrow>
              <Button size="xs" variant="light" color={copied ? 'teal' : 'blue'} leftSection={copied ? <Check size={14} /> : <Copy size={14} />} onClick={copy}>
                {copied ? 'Copied link' : 'Copy link'}
              </Button>
            </Tooltip>
          )}
        </CopyButton>

        <CopyButton value={body} timeout={2000}>
          {({ copied, copy }) => (
            <Tooltip label={copied ? 'Copied email text!' : 'Copy email text'} withArrow>
              <Button size="xs" variant="light" color={copied ? 'teal' : 'gray'} leftSection={copied ? <Check size={14} /> : <Mail size={14} />} onClick={copy}>
                {copied ? 'Copied email' : 'Copy email'}
              </Button>
            </Tooltip>
          )}
        </CopyButton>

        <Tooltip label="Open mail app" withArrow>
          <ActionIcon component="a" href={mailto} variant="subtle" color="blue" size="sm">
            <Mail size={14} />
          </ActionIcon>
        </Tooltip>
      </Group>
    );
  };

  return (
    <>
      <Group justify="space-between" mb="md">
        <Title order={2}>Agreements</Title>
        <Button leftSection={<Plus size={16} />} onClick={() => setOpen(true)}>New agreement</Button>
      </Group>

      {items === null ? <Loader /> : items.length === 0 ? (
        <Text c="dimmed">No agreements yet.</Text>
      ) : (
        <Table striped highlightOnHover withTableBorder>
          <Table.Thead>
            <Table.Tr><Table.Th>Title</Table.Th><Table.Th>Code</Table.Th><Table.Th>Signers</Table.Th><Table.Th>Status</Table.Th><Table.Th w={200}>Actions</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.map((a) => (
              <Table.Tr key={a.id}>
                <Table.Td fw={500}>{a.title}</Table.Td>
                <Table.Td>{a.code}</Table.Td>
                <Table.Td>{a.signedCount} / {a.signerCount}</Table.Td>
                <Table.Td><Badge color={a.isActive ? 'green' : 'gray'}>{a.status}</Badge></Table.Td>
                <Table.Td>
                  <Group gap={6}>
                    <Button size="xs" variant="light" leftSection={<Eye size={14} />} onClick={() => openDetail(a.id)}>Signers</Button>
                    <Button size="xs" variant="outline" onClick={() => handleDownload(a.id)}>PDF</Button>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      {/* New Agreement Modal */}
      <Modal opened={open} onClose={() => setOpen(false)} title="New agreement" size="lg">
        <Stack>
          <Select label="Company" required data={companies.map((c) => ({ value: c.id, label: c.name }))} value={companyId} onChange={setCompanyId} searchable />
          <TextInput label="Title" required value={title} onChange={(e) => setTitle(e.target.value)} />
          <div>
            <Text size="sm" fw={500} mb={4}>PDF</Text>
            <input type="file" accept=".pdf" onChange={(e) => setPdf(e.target.files?.[0] ?? null)} />
          </div>
          <Stack gap={8}>
            <Text size="sm" fw={500}>Signers</Text>
            {signers.map((s, i) => (
              <Group key={i} gap={8}>
                <TextInput placeholder="Name" value={s.name} onChange={(e) => { const n = [...signers]; n[i].name = e.target.value; setSigners(n); }} style={{ flex: 1 }} />
                <TextInput placeholder="Email" value={s.email} onChange={(e) => { const n = [...signers]; n[i].email = e.target.value; setSigners(n); }} style={{ flex: 1 }} />
                <ActionIcon variant="subtle" color="red" onClick={() => setSigners(signers.filter((_, j) => j !== i))}><Trash2 size={16} /></ActionIcon>
              </Group>
            ))}
            <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={() => setSigners([...signers, { name: '', email: '' }])}>Add signer</Button>
          </Stack>
          {error && <Text size="sm" c="red">{error}</Text>}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setOpen(false)}>Cancel</Button>
            <Button loading={submitting} onClick={create}>Create</Button>
          </Group>
        </Stack>
      </Modal>

      {/* Agreement Detail / Signers Modal */}
      <Modal opened={detailModalOpen} onClose={() => { setDetailModalOpen(false); setSelectedAgreement(null); }} title={selectedAgreement?.title ?? 'Agreement Signers'} size="lg">
        {loadingDetail || !selectedAgreement ? (
          <Loader />
        ) : (
          <Stack gap="md">
            <Group justify="space-between">
              <Text size="sm">Code: <b>{selectedAgreement.code}</b></Text>
              <Badge color={selectedAgreement.isActive ? 'green' : 'gray'}>{selectedAgreement.status}</Badge>
            </Group>
            <Text size="sm" fw={500}>Signers ({selectedAgreement.signedCount}/{selectedAgreement.signerCount} signed):</Text>
            <Stack gap="xs">
              {selectedAgreement.signers.map((s) => (
                <Stack key={s.id} p="xs" style={{ border: '1px solid var(--mantine-color-gray-3)', borderRadius: 8 }}>
                  <Group justify="space-between">
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
      <Modal opened={createdAgreement !== null} onClose={() => setCreatedAgreement(null)} title="Agreement Created Successfully" size="lg">
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
