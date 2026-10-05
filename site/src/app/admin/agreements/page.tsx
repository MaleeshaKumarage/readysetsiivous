'use client';

import { useCallback, useEffect, useState } from 'react';
import { Title, Button, Table, Modal, TextInput, Select, Group, Stack, Text, Loader, Badge, ActionIcon } from '@mantine/core';
import { Plus, Trash2, Copy } from 'lucide-react';
import { adminAgreements, adminCompanies, type AgreementListItem, type Company } from '@/lib/adminApi';

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

  const load = useCallback(async () => { setItems(await adminAgreements.list()); }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); }); }, []);

  const create = async () => {
    const validSigners = signers.filter((s) => s.name.trim() && s.email.trim());
    if (!pdf || !companyId || !title.trim()) { setError('Company, title and PDF are required.'); return; }
    if (validSigners.length === 0) { setError('At least one signer with name and email is required.'); return; }
    setSubmitting(true); setError('');
    const ok = await adminAgreements.create(title.trim(), companyId, pdf, validSigners);
    setSubmitting(false);
    if (!ok) { setError('Failed to create agreement.'); return; }
    setOpen(false); setTitle(''); setCompanyId(null); setPdf(null); setSigners([{ name: '', email: '' }]);
    load();
  };

  const handleDownload = (id: string) => {
    adminAgreements.downloadDocument(id);
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
            <Table.Tr><Table.Th>Title</Table.Th><Table.Th>Code</Table.Th><Table.Th>Signers</Table.Th><Table.Th>Status</Table.Th><Table.Th w={140}>Actions</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.map((a) => (
              <Table.Tr key={a.id}>
                <Table.Td fw={500}>{a.title}</Table.Td>
                <Table.Td>{a.code}</Table.Td>
                <Table.Td>{a.signedCount} / {a.signerCount}</Table.Td>
                <Table.Td><Badge color={a.isActive ? 'green' : 'gray'}>{a.status}</Badge></Table.Td>
                <Table.Td>
                  <Button size="xs" variant="light" onClick={() => handleDownload(a.id)}>PDF</Button>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

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
    </>
  );
}
