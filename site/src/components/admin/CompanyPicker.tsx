'use client';

import { useEffect, useRef, useState } from 'react';
import { Button, Combobox, Group, InputBase, Loader, ScrollArea, Text, useCombobox } from '@mantine/core';
import { adminCompanies, type Company } from '@/lib/adminApi';

const PAGE_SIZE = 50;
const label = (company: Company) => `${company.name}${company.isActive === false ? ' (inactive)' : ''}`;

/** A bounded, server-searched company selector. Selection is independent of result pages. */
export function CompanyPicker({ value, onChange, required, allowInactive = true }: {
  value: string | null;
  onChange: (value: string | null) => void;
  required?: boolean;
  allowInactive?: boolean;
}) {
  const [search, setSearch] = useState('');
  const [rows, setRows] = useState<Company[]>([]);
  const [total, setTotal] = useState(0);
  const [offset, setOffset] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [selected, setSelected] = useState<Company | null>(null);
  const [selectionError, setSelectionError] = useState('');
  const [selectionAttempt, setSelectionAttempt] = useState(0);
  const generation = useRef(0);
  const companies = useRef(new Map<string, Company>());
  const currentSearch = useRef('');
  const retryOffset = useRef(0);
  const store = useCombobox({
    onDropdownOpen: () => store.focusSearchInput(),
    onDropdownClose: () => { store.resetSelectedOption(); store.focusTarget(); },
  });

  useEffect(() => {
    let active = true;
    setSelectionError('');
    setSelected(value ? companies.current.get(value) ?? null : null);
    if (value && !companies.current.has(value)) {
      void adminCompanies.get(value).then(detail => {
        if (!active) return;
        if (!detail?.company || detail.company.id !== value) throw new Error('unavailable');
        companies.current.set(value, detail.company); setSelected(detail.company);
      }).catch(() => { if (active) setSelectionError('The selected company could not be loaded. Your selection is kept.'); });
    }
    return () => { active = false; };
  }, [value, selectionAttempt]);

  async function load(query: string, skip: number) {
    const version = ++generation.current; retryOffset.current = skip;
    setLoading(true); setError('');
    try {
      const result = await adminCompanies.list(query, skip, PAGE_SIZE);
      if (version !== generation.current) return;
      if (!result || !Array.isArray(result.items) || !Number.isInteger(result.total) || result.total < 0
          || result.items.some(item => !item?.id || typeof item.name !== 'string')
          || (result.items.length === 0 && skip < result.total)) throw new Error('incomplete');
      result.items.forEach(company => companies.current.set(company.id, company));
      setRows(previous => skip ? [...new Map([...previous, ...result.items].map(item => [item.id, item])).values()] : result.items);
      setTotal(result.total); setOffset(skip + result.items.length);
      store.resetSelectedOption();
    } catch {
      if (version === generation.current) setError('Could not load companies. Retry to keep searching.');
    } finally { if (version === generation.current) setLoading(false); }
  }

  useEffect(() => {
    if (!store.dropdownOpened) return;
    const timer = setTimeout(() => void load(search.trim(), 0), search ? 250 : 0);
    return () => { clearTimeout(timer); generation.current++; };
    // Opening or changing the query starts a fresh page, not a fetch-all loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, store.dropdownOpened]);

  const changeSearch = (query: string) => {
    generation.current++; currentSearch.current = query.trim();
    setSearch(query); setRows([]); setOffset(0); setTotal(0); setError(''); setLoading(true);
    store.resetSelectedOption();
  };

  return <div>
    <Combobox store={store} withinPortal onOptionSubmit={id => {
      const company = companies.current.get(id);
      if (!company || (!allowInactive && company.isActive === false && id !== value)) return;
      setSelected(company); onChange(id); store.closeDropdown();
    }}>
      <Combobox.Target withExpandedAttribute>
        <InputBase label="Company" required={required} readOnly value={selected ? label(selected) : value ? selectionError ? 'Selected company (unavailable)' : 'Selected company (loading)' : ''}
          placeholder="Select company" rightSection={<Combobox.Chevron />} rightSectionPointerEvents="none"
          onClick={() => store.openDropdown()} aria-busy={Boolean(value && !selected && !selectionError)} />
      </Combobox.Target>
      <Combobox.Dropdown>
        <Combobox.Search aria-label="Search companies" placeholder="Search name or business ID" value={search}
          onChange={event => changeSearch(event.currentTarget.value)} />
        <Combobox.Options>
          <ScrollArea.Autosize mah={240} type="auto">
            {rows.map(company => <Combobox.Option key={company.id} value={company.id} aria-label={label(company)}
              active={value === company.id} disabled={!allowInactive && company.isActive === false && company.id !== value}
              aria-disabled={!allowInactive && company.isActive === false && company.id !== value}>
              <Text size="sm">{label(company)}</Text>{company.businessId && <Text size="xs" c="dimmed">{company.businessId}</Text>}
            </Combobox.Option>)}
            {!loading && !error && !rows.length && <Combobox.Empty>No companies found.</Combobox.Empty>}
          </ScrollArea.Autosize>
        </Combobox.Options>
        <Combobox.Footer>
          {error && <Text role="alert" size="sm" c="red">{error}</Text>}
          <Group justify="space-between" gap="xs">
            <Text size="xs" c="dimmed" role="status">{loading ? 'Loading companies…' : `${rows.length} of ${total} companies`}</Text>
            {loading && <Loader size="xs" />}
            {!loading && error && <Button size="xs" variant="light" onClick={() => void load(currentSearch.current, retryOffset.current)}>Retry companies</Button>}
            {!loading && !error && offset < total && <Button size="xs" variant="light" onClick={() => void load(currentSearch.current, offset)}>Load more companies</Button>}
          </Group>
        </Combobox.Footer>
      </Combobox.Dropdown>
    </Combobox>
    {selectionError && <Group gap="xs"><Text role="alert" size="sm" c="red">{selectionError}</Text><Button size="xs" variant="subtle" onClick={() => setSelectionAttempt(attempt => attempt + 1)}>Retry selected company</Button></Group>}
  </div>;
}
