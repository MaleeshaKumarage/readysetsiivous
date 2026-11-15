'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { Badge, Button, Group, Loader, Stack, Table, Tabs, Text, Title } from '@mantine/core';
import { ArrowLeft, Plus } from 'lucide-react';
import { adminAgreements, adminInvoices, adminShifts, authorizedFetch, type AgreementListItem, type CompanyDetail, type Invoice, type Shift } from '@/lib/adminApi';
import { LifecycleTabs, inLifecycle, type LifecycleStatus } from '@/components/LifecycleTabs';

// Expenses and reports are not shown: E01 is not implemented and report shares store the company only inside report JSON.
const SECTIONS = ['overview', 'properties', 'agreements', 'work', 'invoices'] as const;
type Section = (typeof SECTIONS)[number];
const SECTION_LABELS: Record<Section, string> = { overview: 'Overview', properties: 'Properties', agreements: 'Agreements', work: 'Work', invoices: 'Invoices' };
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const money = (value: number) => new Intl.NumberFormat('en', { style: 'currency', currency: 'EUR' }).format(value);
const dateOnly = (value?: string | null) => (value ? value.slice(0, 10) : '—');

type Route = { id: string; section: Section; back: string };
type Panel<T> = { status: 'loading' | 'ready' | 'error'; items: T[] };
type Detail = { status: 'loading' } | { status: 'ready'; data: CompanyDetail } | { status: 'missing' } | { status: 'error' };

const readRoute = (): Route => {
  const params = new URLSearchParams(window.location.search);
  const requested = params.get('section') ?? '';
  const section = (SECTIONS as readonly string[]).includes(requested) ? (requested as Section) : 'overview';
  return { id: params.get('id') ?? '', section, back: params.get('back') ?? '' };
};

/** Rebuilds the companies list URL from the back parameter, keeping only keys the list understands. */
const listHref = (back: string) => {
  const params = new URLSearchParams(back);
  const next = new URLSearchParams();
  if (params.get('status') === 'inactive') next.set('status', 'inactive');
  const page = Number(params.get('page'));
  if (Number.isInteger(page) && page > 0) next.set('page', String(page));
  const size = Number(params.get('size'));
  if ([25, 50, 100].includes(size)) next.set('size', String(size));
  const q = params.get('q')?.trim();
  if (q) next.set('q', q);
  const text = next.toString();
  return `/admin/companies/${text ? `?${text}` : ''}`;
};

export default function CustomerWorkspacePage() {
  const [route, setRoute] = useState<Route | null>(null);
  const [detail, setDetail] = useState<Detail>({ status: 'loading' });
  const [agreements, setAgreements] = useState<Panel<AgreementListItem> | null>(null);
  const [shifts, setShifts] = useState<Panel<Shift> | null>(null);
  const [invoices, setInvoices] = useState<Panel<Invoice> | null>(null);
  const [workStatus, setWorkStatus] = useState<LifecycleStatus>('active');
  // The customer currently shown; responses for any other customer or an older request are ignored.
  const current = useRef<string | null>(null);
  const generation = useRef({ detail: 0, agreements: 0, work: 0, invoices: 0 });

  const loadDetail = async (id: string) => {
    const version = ++generation.current.detail;
    setDetail({ status: 'loading' });
    let next: Detail;
    if (!GUID.test(id)) next = { status: 'missing' };
    else {
      try {
        const response = await authorizedFetch(`/api/v1/admin/companies/${id}`, { cache: 'no-store' });
        if (response.status === 404) next = { status: 'missing' };
        else if (!response.ok) next = { status: 'error' };
        else next = { status: 'ready', data: (await response.json()) as CompanyDetail };
      } catch {
        next = { status: 'error' };
      }
    }
    if (version === generation.current.detail && current.current === id) setDetail(next);
  };

  const loadAgreements = async (id: string) => {
    const version = ++generation.current.agreements;
    setAgreements({ status: 'loading', items: [] });
    const items = await adminAgreements.listForCustomer(id).catch(() => null);
    if (version === generation.current.agreements && current.current === id) setAgreements(items ? { status: 'ready', items } : { status: 'error', items: [] });
  };

  const loadShifts = async (id: string) => {
    const version = ++generation.current.work;
    setShifts({ status: 'loading', items: [] });
    const items = await adminShifts.listForCustomer(id).catch(() => null);
    if (version === generation.current.work && current.current === id) setShifts(items ? { status: 'ready', items } : { status: 'error', items: [] });
  };

  const loadInvoices = async (id: string) => {
    const version = ++generation.current.invoices;
    setInvoices({ status: 'loading', items: [] });
    const items = await adminInvoices.listForCustomer(id).catch(() => null);
    if (version === generation.current.invoices && current.current === id) setInvoices(items ? { status: 'ready', items } : { status: 'error', items: [] });
  };

  // Browser Back/Forward and direct loads both come through the URL.
  useEffect(() => {
    const read = () => setRoute(readRoute());
    read();
    window.addEventListener('popstate', read);
    return () => window.removeEventListener('popstate', read);
  }, []);

  useEffect(() => {
    if (!route || current.current === route.id) return;
    current.current = route.id;
    setAgreements(null); setShifts(null); setInvoices(null); setWorkStatus('active');
    void loadDetail(route.id);
  }, [route]);

  // Each secondary tab loads only when opened, and only after the customer itself is known.
  useEffect(() => {
    if (!route || detail.status !== 'ready') return;
    if (route.section === 'agreements' && agreements === null) void loadAgreements(route.id);
    if (route.section === 'work' && shifts === null) void loadShifts(route.id);
    if (route.section === 'invoices' && invoices === null) void loadInvoices(route.id);
  }, [route, detail, agreements, shifts, invoices]);

  const changeSection = (section: Section) => {
    if (!route || section === route.section) return;
    const params = new URLSearchParams({ id: route.id, section });
    if (route.back) params.set('back', route.back);
    window.history.pushState(null, '', `${window.location.pathname}?${params.toString()}`);
    setRoute({ ...route, section });
  };

  if (!route) return <Loader />;
  const backHref = listHref(route.back);

  return (
    <>
      <Group justify="space-between" align="center" mb="md" wrap="wrap" gap="sm">
        <Button variant="subtle" size="xs" leftSection={<ArrowLeft size={14} />} component={Link} href={backHref}>Back to customers</Button>
      </Group>

      {detail.status === 'loading' && <Loader />}
      {detail.status === 'missing' && (
        <Stack>
          <Title order={2}>Customer not found</Title>
          <Text c="dimmed">This customer does not exist or is not available to your account.</Text>
        </Stack>
      )}
      {detail.status === 'error' && (
        <Stack>
          <Text role="alert" c="red">Customer could not be loaded.</Text>
          <Button variant="light" onClick={() => void loadDetail(route.id)}>Retry customer</Button>
        </Stack>
      )}

      {detail.status === 'ready' && (() => {
        const { company, branches } = detail.data;
        const branchName = new Map(branches.map(branch => [branch.id, branch.name]));
        const activeBranches = branches.filter(branch => branch.isActive).length;
        return (
          <>
            <Title order={2}>{company.name}</Title>
            <Group gap="xs" mb="sm" wrap="wrap">
              <Text size="sm" c="dimmed">Business ID {company.businessId}</Text>
              <Badge color={company.isActive ? 'green' : 'gray'}>{company.isActive ? 'Active' : 'Inactive'}</Badge>
              {company.isPartnerEnabled && <Badge variant="light">Partner access</Badge>}
            </Group>

            <Tabs value={route.section} onChange={value => value && changeSection(value as Section)}>
              <Tabs.List aria-label="Customer sections" style={{ flexWrap: 'wrap' }}>
                {SECTIONS.map(section => <Tabs.Tab key={section} value={section}>{SECTION_LABELS[section]}</Tabs.Tab>)}
              </Tabs.List>

              <Tabs.Panel value="overview" pt="md">
                <Stack gap="xs">
                  <Text size="sm">Contact: {company.contactName || '—'}{company.contactEmail ? ` · ${company.contactEmail}` : ''}{company.contactPhone ? ` · ${company.contactPhone}` : ''}</Text>
                  <Text size="sm">Partner access: {company.isPartnerEnabled ? 'Enabled' : 'Not enabled'}</Text>
                  <Text size="sm">Properties: {activeBranches} active · {branches.length - activeBranches} inactive</Text>
                  <Group gap="xs">
                    <Button size="xs" variant="light" component={Link} href={`/admin/companies/?company=${company.id}`}>Edit in companies list</Button>
                  </Group>
                </Stack>
              </Tabs.Panel>

              <Tabs.Panel value="properties" pt="md">
                <Stack gap="xs">
                  <Group justify="space-between" wrap="wrap" gap="xs">
                    <Text size="sm" fw={600}>Properties</Text>
                    <Button size="xs" variant="light" component={Link} href={`/admin/companies/?company=${company.id}`}>Manage properties</Button>
                  </Group>
                  {branches.length === 0 ? <Text size="sm" c="dimmed">No properties yet.</Text> : branches.map(branch => (
                    <Group key={branch.id} gap="xs" wrap="wrap">
                      <Text size="sm">{branch.name}{branch.city ? ` · ${branch.city}` : ''}</Text>
                      {!branch.isActive && <Badge color="gray" size="xs">Inactive</Badge>}
                    </Group>
                  ))}
                </Stack>
              </Tabs.Panel>

              <Tabs.Panel value="agreements" pt="md">
                <Stack gap="sm">
                  <Group justify="space-between" wrap="wrap" gap="xs">
                    <Text size="sm" fw={600}>Agreements</Text>
                    <Button size="xs" variant="light" leftSection={<Plus size={14} />} component={Link} href={`/admin/agreements/?company=${company.id}`}>New agreement</Button>
                  </Group>
                  {!agreements || agreements.status === 'loading' ? <Loader size="sm" /> : agreements.status === 'error' ? (
                    <Stack><Text role="alert" c="red">Agreements could not be loaded.</Text><Button variant="light" size="xs" onClick={() => void loadAgreements(company.id)}>Retry agreements</Button></Stack>
                  ) : agreements.items.length === 0 ? <Text size="sm" c="dimmed">No agreements for this customer yet.</Text> : (
                    <Table.ScrollContainer minWidth={420}>
                      <Table striped withTableBorder>
                        <Table.Thead><Table.Tr><Table.Th>Title</Table.Th><Table.Th>Code</Table.Th><Table.Th>Signers</Table.Th><Table.Th>Status</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>
                          {agreements.items.map(agreement => (
                            <Table.Tr key={agreement.id}>
                              <Table.Td>{agreement.title}</Table.Td>
                              <Table.Td>{agreement.code}</Table.Td>
                              <Table.Td>{agreement.signedCount} / {agreement.signerCount}</Table.Td>
                              <Table.Td><Badge color={agreement.isActive ? 'green' : 'gray'}>{agreement.isActive ? agreement.status : `Inactive · ${agreement.status}`}</Badge></Table.Td>
                            </Table.Tr>
                          ))}
                        </Table.Tbody>
                      </Table>
                    </Table.ScrollContainer>
                  )}
                </Stack>
              </Tabs.Panel>

              <Tabs.Panel value="work" pt="md">
                <Stack gap="sm">
                  <Group justify="space-between" wrap="wrap" gap="xs">
                    <Text size="sm" fw={600}>Shifts</Text>
                    <Button size="xs" variant="light" leftSection={<Plus size={14} />} component={Link} href={`/admin/shifts/?company=${company.id}`}>Add shift</Button>
                  </Group>
                  <LifecycleTabs value={workStatus} onChange={setWorkStatus} label="Shift activity"
                    activeCount={shifts?.status === 'ready' ? shifts.items.filter(shift => inLifecycle(shift, 'active')).length : undefined}
                    inactiveCount={shifts?.status === 'ready' ? shifts.items.filter(shift => inLifecycle(shift, 'inactive')).length : undefined}>
                    {!shifts || shifts.status === 'loading' ? <Loader size="sm" /> : shifts.status === 'error' ? (
                      <Stack><Text role="alert" c="red">Shifts could not be loaded.</Text><Button variant="light" size="xs" onClick={() => void loadShifts(company.id)}>Retry shifts</Button></Stack>
                    ) : (() => {
                      const visible = shifts.items.filter(shift => inLifecycle(shift, workStatus));
                      if (visible.length === 0) return <Text size="sm" c="dimmed">{shifts.items.length === 0 ? 'No shifts for this customer yet.' : `No ${workStatus} shifts.`}</Text>;
                      return (
                        <Table.ScrollContainer minWidth={520}>
                          <Table striped withTableBorder>
                            <Table.Thead><Table.Tr><Table.Th>Name</Table.Th><Table.Th>Property</Table.Th><Table.Th>Status</Table.Th><Table.Th>Valid from</Table.Th><Table.Th>Valid until</Table.Th></Table.Tr></Table.Thead>
                            <Table.Tbody>
                              {visible.map(shift => (
                                <Table.Tr key={shift.id}>
                                  <Table.Td>{shift.name}</Table.Td>
                                  <Table.Td>{branchName.get(shift.branchId) ?? '—'}</Table.Td>
                                  <Table.Td>{shift.isCompleted ? 'Completed' : shift.isActive ? 'Active' : 'Inactive'}</Table.Td>
                                  <Table.Td>{dateOnly(shift.validFrom)}</Table.Td>
                                  <Table.Td>{dateOnly(shift.validUntil)}</Table.Td>
                                </Table.Tr>
                              ))}
                            </Table.Tbody>
                          </Table>
                        </Table.ScrollContainer>
                      );
                    })()}
                  </LifecycleTabs>
                </Stack>
              </Tabs.Panel>

              <Tabs.Panel value="invoices" pt="md">
                <Stack gap="sm">
                  <Group justify="space-between" wrap="wrap" gap="xs">
                    <Text size="sm" fw={600}>Invoices</Text>
                    <Button size="xs" variant="light" component={Link} href="/admin/invoices/">Open invoices</Button>
                  </Group>
                  {!invoices || invoices.status === 'loading' ? <Loader size="sm" /> : invoices.status === 'error' ? (
                    <Stack><Text role="alert" c="red">Invoices could not be loaded.</Text><Button variant="light" size="xs" onClick={() => void loadInvoices(company.id)}>Retry invoices</Button></Stack>
                  ) : invoices.items.length === 0 ? <Text size="sm" c="dimmed">No invoices for this customer yet.</Text> : (
                    <Table.ScrollContainer minWidth={520}>
                      <Table striped withTableBorder>
                        <Table.Thead><Table.Tr><Table.Th>Invoice</Table.Th><Table.Th>Issued</Table.Th><Table.Th>Due</Table.Th><Table.Th>Status</Table.Th><Table.Th>Total</Table.Th></Table.Tr></Table.Thead>
                        <Table.Tbody>
                          {invoices.items.map(invoice => (
                            <Table.Tr key={invoice.id}>
                              <Table.Td>{invoice.invoiceNumber}</Table.Td>
                              <Table.Td>{dateOnly(invoice.issueDate)}</Table.Td>
                              <Table.Td>{dateOnly(invoice.dueDate)}</Table.Td>
                              <Table.Td>{invoice.status}</Table.Td>
                              <Table.Td>{money(invoice.total.gross)}</Table.Td>
                            </Table.Tr>
                          ))}
                        </Table.Tbody>
                      </Table>
                    </Table.ScrollContainer>
                  )}
                </Stack>
              </Tabs.Panel>
            </Tabs>
          </>
        );
      })()}
    </>
  );
}
