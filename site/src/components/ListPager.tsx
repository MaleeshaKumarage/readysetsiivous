'use client';

import { Button, Group, Text } from '@mantine/core';

export const PAGE_SIZE = 25;

/** Returns one page of items. Page numbers are 1-based and clamped to the last available page. */
export function paginate<T>(items: T[], page: number, size = PAGE_SIZE): { pageItems: T[]; pageCount: number; page: number } {
  const pageCount = Math.max(1, Math.ceil(items.length / size));
  const current = Math.min(Math.max(1, page), pageCount);
  return { pageItems: items.slice((current - 1) * size, current * size), pageCount, page: current };
}

/** Previous/next controls for a client-paged list. Renders nothing when everything fits on one page. */
export function ListPager({ label, total, page, pageCount, onPage }: {
  label: string;
  total: number;
  page: number;
  pageCount: number;
  onPage: (page: number) => void;
}) {
  if (total <= PAGE_SIZE) return null;
  const from = (page - 1) * PAGE_SIZE + 1;
  const to = Math.min(total, page * PAGE_SIZE);
  return <Group justify="space-between" mt="sm" wrap="wrap" gap="sm" aria-label={`${label} pages`} role="navigation">
    <Text size="sm">Showing {from}–{to} of {total}</Text>
    <Group gap="xs">
      <Button variant="default" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</Button>
      <Text size="sm">Page {page} of {pageCount}</Text>
      <Button variant="default" size="sm" disabled={page >= pageCount} onClick={() => onPage(page + 1)}>Next</Button>
    </Group>
  </Group>;
}
