import { Stack, Table, Text } from '@mantine/core';

export type PriceVersionRow = { effectiveFrom: string; priceNet: number };

// Dates come from the API as yyyy-mm-dd; the admin reads them as dd/mm/yyyy.
export function formatPriceDate(iso: string): string {
  const [year, month, day] = iso.split('-');
  return `${day}/${month}/${year}`;
}

/**
 * Newest first. Each version runs until the next one starts; the current version runs to Present.
 * Pure so the ordering and ranges can be checked without rendering.
 */
export function priceHistoryRows(versions: PriceVersionRow[]) {
  const sorted = [...versions].sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom));
  return sorted
    .map((version, index) => {
      const next = sorted[index + 1];
      return {
        key: version.effectiveFrom,
        range: `${formatPriceDate(version.effectiveFrom)} - ${next ? formatPriceDate(next.effectiveFrom) : 'Present'}`,
        price: `EUR ${version.priceNet.toFixed(2)}`,
      };
    })
    .reverse();
}

export function BranchPriceHistory({ branchName, versions }: { branchName: string; versions: PriceVersionRow[] }) {
  if (versions.length === 0) return null;
  const rows = priceHistoryRows(versions);
  return (
    <Stack gap={4}>
      <Text size="sm" fw={600}>Price history</Text>
      <Table aria-label={`Price history for ${branchName}`} withTableBorder withColumnBorders={false} verticalSpacing={4} horizontalSpacing="sm" fz="xs" data-testid="price-history-table">
        <Table.Thead>
          <Table.Tr><Table.Th>Period</Table.Th><Table.Th>Price excl. VAT</Table.Th></Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map(row => (
            <Table.Tr key={row.key} data-testid="price-history-row">
              <Table.Td>{row.range}</Table.Td>
              <Table.Td fw={600}>{row.price}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Stack>
  );
}
