'use client';
import { useState } from 'react';
import { Button, Group, Paper, Stack, Text, TextInput } from '@mantine/core';
import type { LeadBranchFields } from '@/lib/leadsApi';
export function LeadBranchesEditor({ branches, onChange, disabled = false, readOnly = false }: { branches: LeadBranchFields[]; onChange?: (branches: LeadBranchFields[]) => void; disabled?: boolean; readOnly?: boolean }) {
  const [page, setPage] = useState(0); const current = Math.min(page, Math.max(0, Math.ceil(branches.length / 5) - 1));
  const set = (id: string, key: keyof LeadBranchFields, value: string) => onChange?.(branches.map(branch => branch.id === id ? { ...branch, [key]: value } : branch));
  const fields: [keyof LeadBranchFields, string, number][] = [['name', 'name', 200], ['street', 'street', 300], ['postalCode', 'postal code', 30], ['city', 'city', 100], ['country', 'country', 100], ['contactPhone', 'phone', 50], ['contactEmail', 'email', 254]];
  return <Stack gap="sm">{!branches.length && <Text>No branches added. A lead can be saved and converted without a branch.</Text>}
    {!readOnly && <Button variant="light" disabled={disabled} onClick={() => { onChange?.([...branches, { id: crypto.randomUUID(), name: '', street: '', postalCode: '', city: '', country: 'FI', contactPhone: '', contactEmail: '' }]); setPage(Math.floor(branches.length / 5)); }}>Add branch</Button>}
    {branches.slice(current * 5, current * 5 + 5).map((branch, index) => { const number = current * 5 + index + 1; return <Paper key={branch.id} withBorder p="sm"><Stack gap="xs"><Text fw={600}>Branch {number}{branch.name ? ` · ${branch.name}` : ''}</Text>
      {readOnly ? <><Text>{[branch.street, branch.postalCode, branch.city, branch.country].filter(Boolean).join(', ') || 'No address provided'}</Text><Text>{[branch.contactPhone, branch.contactEmail].filter(Boolean).join(' · ') || 'No branch contact provided'}</Text></> : <>{fields.map(([key, label, max]) => <TextInput key={key} label={`Branch ${number} ${label}`} required={key === 'name'} maxLength={max} value={branch[key]} disabled={disabled} onChange={e => set(branch.id, key, e.target.value)} />)}<Button variant="subtle" color="red" disabled={disabled} onClick={() => { if (window.confirm(`Remove branch ${number} from this lead? Save the lead to apply this change.`)) onChange?.(branches.filter(b => b.id !== branch.id)); }}>Remove branch {number}</Button></>}
    </Stack></Paper>; })}
    {branches.length > 5 && <Group><Button size="xs" disabled={!current || disabled} onClick={() => setPage(current - 1)}>Previous branches</Button><Text size="sm">{current * 5 + 1}–{Math.min(current * 5 + 5, branches.length)} of {branches.length} branches</Text><Button size="xs" disabled={(current + 1) * 5 >= branches.length || disabled} onClick={() => setPage(current + 1)}>Next branches</Button></Group>}
  </Stack>;
}
