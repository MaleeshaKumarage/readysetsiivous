'use client';

import { Button, Group, NumberInput, Paper, Stack, Text, TextInput } from '@mantine/core';

export type EditableInvoiceRow = { id: string; description: string; quantity: number | string; unitPriceNet: number | string };
export const newInvoiceRow = (): EditableInvoiceRow => ({ id: crypto.randomUUID(), description: '', quantity: 1, unitPriceNet: '' });

export default function ManualInvoiceRows({ rows, onChange, vatRate }: {
  rows: EditableInvoiceRow[]; onChange: (rows: EditableInvoiceRow[]) => void; vatRate: number;
}) {
  const patch = (id: string, values: Partial<EditableInvoiceRow>) => onChange(rows.map(row => row.id === id ? { ...row, ...values } : row));
  return <Stack>
    <Text size="sm">Enter agreed prices before VAT. VAT ({vatRate}%) is added using your saved company settings.</Text>
    {rows.map((row, index) => <Paper key={row.id} withBorder p="sm"><Stack gap="sm">
      <Text fw={600}>Row {index + 1}</Text>
      <TextInput required label={`Row ${index + 1} description`} maxLength={1000} value={row.description} onChange={event => patch(row.id, { description: event.currentTarget.value })} />
      <Group className="admin-invoice-fields" grow preventGrowOverflow={false}>
        <NumberInput required label={`Row ${index + 1} quantity (pieces)`} min={0.01} max={10000} decimalScale={2} value={row.quantity} onChange={quantity => patch(row.id, { quantity })} />
        <NumberInput required label={`Row ${index + 1} unit price before VAT (€)`} min={0} max={1000000} decimalScale={2} value={row.unitPriceNet} onChange={unitPriceNet => patch(row.id, { unitPriceNet })} />
      </Group>
      <Button variant="subtle" color="red" aria-label={`Remove row ${index + 1}`} disabled={rows.length === 1} onClick={() => onChange(rows.filter(value => value.id !== row.id))}>Remove row</Button>
    </Stack></Paper>)}
    <Button variant="light" disabled={rows.length >= 100} onClick={() => onChange([...rows, newInvoiceRow()])}>Add row</Button>
  </Stack>;
}
