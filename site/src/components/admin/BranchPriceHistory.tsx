import { Stack, Text } from '@mantine/core';

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
    <Stack gap={2} role="list" aria-label={`Price history for ${branchName}`}>
      <Text size="xs" fw={600}>Price history</Text>
      {rows.map(row => (
        <Text key={row.key} role="listitem" size="xs" data-testid="price-history-row">
          {row.range} <Text component="span" size="xs" fw={600}>{row.price}</Text>
        </Text>
      ))}
    </Stack>
  );
}
