'use client';

import { useCallback, useEffect, useState } from 'react';
import { Plus } from 'lucide-react';
import { adminServices } from '@/lib/adminApi';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger,
} from '@/components/ui/dialog';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';

interface ServiceRow {
  id: string;
  slug: string;
  name: { values: Record<string, string> };
  description: { values: Record<string, string> };
  durationMinutes: number;
  priceNet: number;
  vatRatePercent: number;
  isActive: boolean;
  isFeatured: boolean;
  sortOrder: number;
}

const EMPTY = {
  slug: '',
  category: 'home',
  nameFi: '',
  nameEn: '',
  nameSv: '',
  descriptionFi: '',
  descriptionEn: '',
  descriptionSv: '',
  durationMinutes: 120,
  priceNet: 0,
  vatRatePercent: 25.5,
  isFeatured: false,
  sortOrder: 0,
};

export default function ServicesPage() {
  const [services, setServices] = useState<ServiceRow[] | null>(null);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState(EMPTY);

  const load = useCallback(async () => {
    setServices(await adminServices.list(true));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function save() {
    await adminServices.create({
      slug: form.slug,
      category: form.category,
      name: { fi: form.nameFi, en: form.nameEn, sv: form.nameSv },
      description: { fi: form.descriptionFi, en: form.descriptionEn, sv: form.descriptionSv },
      durationMinutes: Number(form.durationMinutes),
      priceNet: Number(form.priceNet),
      vatRatePercent: Number(form.vatRatePercent),
      currency: 'EUR',
      isFeatured: form.isFeatured,
      sortOrder: Number(form.sortOrder),
    });
    setForm(EMPTY);
    setOpen(false);
    load();
  }

  async function toggleActive(s: ServiceRow) {
    await adminServices.update(
      s.id,
      {
        slug: s.slug,
        category: '',
        name: s.name.values,
        description: s.description.values,
        durationMinutes: s.durationMinutes,
        priceNet: s.priceNet,
        vatRatePercent: s.vatRatePercent,
        currency: 'EUR',
        isFeatured: s.isFeatured,
        sortOrder: s.sortOrder,
      },
      !s.isActive
    );
    load();
  }

  const set = (key: keyof typeof EMPTY) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Services</h1>
          <p className="text-sm text-muted-foreground">Cleaning services shown on the website.</p>
        </div>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger className="inline-flex items-center gap-2 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground shadow hover:bg-primary/90">
            <Plus className="h-4 w-4" />
            Add service
          </DialogTrigger>
          <DialogContent className="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>New service</DialogTitle>
              <DialogDescription>Prices update on the website immediately.</DialogDescription>
            </DialogHeader>
            <div className="grid gap-4 py-2 sm:grid-cols-3">
              <Field label="Slug" value={form.slug} onChange={set('slug')} />
              <Field label="Category" value={form.category} onChange={set('category')} />
              <Field label="Name (fi)" value={form.nameFi} onChange={set('nameFi')} />
              <Field label="Name (en)" value={form.nameEn} onChange={set('nameEn')} />
              <Field label="Name (sv)" value={form.nameSv} onChange={set('nameSv')} />
              <Field label="Description (fi)" value={form.descriptionFi} onChange={set('descriptionFi')} />
              <Field label="Description (en)" value={form.descriptionEn} onChange={set('descriptionEn')} />
              <Field label="Description (sv)" value={form.descriptionSv} onChange={set('descriptionSv')} />
              <Field label="Duration (min)" value={String(form.durationMinutes)} onChange={set('durationMinutes')} />
              <Field label="Price net (€)" value={String(form.priceNet)} onChange={set('priceNet')} />
              <Field label="VAT %" value={String(form.vatRatePercent)} onChange={set('vatRatePercent')} />
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
              <Button onClick={save}>Create</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>

      <div className="rounded-xl border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name (fi)</TableHead>
              <TableHead>Slug</TableHead>
              <TableHead>Duration</TableHead>
              <TableHead>Price net</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">Active</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {services?.map((s) => (
              <TableRow key={s.id}>
                <TableCell className="font-medium">{s.name.values.fi}</TableCell>
                <TableCell className="text-muted-foreground">{s.slug}</TableCell>
                <TableCell>{s.durationMinutes} min</TableCell>
                <TableCell>{s.priceNet} €</TableCell>
                <TableCell>
                  {s.isActive
                    ? <Badge variant="default">Active</Badge>
                    : <Badge variant="secondary">Inactive</Badge>}
                </TableCell>
                <TableCell className="text-right">
                  <Switch checked={s.isActive} onCheckedChange={() => toggleActive(s)} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

function Field({ label, value, onChange }: { label: string; value: string; onChange: (e: React.ChangeEvent<HTMLInputElement>) => void }) {
  return (
    <div className="space-y-1.5">
      <Label className="text-xs">{label}</Label>
      <Input value={value} onChange={onChange} />
    </div>
  );
}
