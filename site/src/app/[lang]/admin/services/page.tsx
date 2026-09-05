'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { ArrowDown, ArrowUp, Pencil, Plus, Upload } from 'lucide-react';
import { adminServices } from '@/lib/adminApi';
import { SERVICE_ICONS, serviceIcon } from '@/lib/icons';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';

interface ServiceRow {
  id: string;
  slug: string;
  category: string;
  name: { values: Record<string, string> };
  description: { values: Record<string, string> };
  additionalInfo: { values: Record<string, string> } | null;
  icon: string;
  imageUrl: string;
  durationMinutes: number;
  priceNet: number;
  vatRatePercent: number;
  isActive: boolean;
  isFeatured: boolean;
  sortOrder: number;
}

interface FormState {
  slug: string;
  category: string;
  nameFi: string;
  nameEn: string;
  nameSv: string;
  descriptionFi: string;
  descriptionEn: string;
  descriptionSv: string;
  additionalFi: string;
  additionalEn: string;
  additionalSv: string;
  priceNet: number;
  vatRatePercent: number;
  icon: string;
  imageUrl: string;
  isFeatured: boolean;
  sortOrder: number;
}

const EMPTY: FormState = {
  slug: '',
  category: 'home',
  nameFi: '', nameEn: '', nameSv: '',
  descriptionFi: '', descriptionEn: '', descriptionSv: '',
  additionalFi: '', additionalEn: '', additionalSv: '',
  priceNet: 0,
  vatRatePercent: 25.5,
  icon: 'Sparkles',
  imageUrl: '',
  isFeatured: false,
  sortOrder: 0,
};

export default function ServicesPage() {
  const [services, setServices] = useState<ServiceRow[] | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<ServiceRow | null>(null);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    const list = await adminServices.list(true);
    setServices(list ? [...list].sort((a, b) => a.sortOrder - b.sortOrder) : null);
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  function openCreate() {
    setEditing(null);
    setForm({ ...EMPTY, sortOrder: services?.length ?? 0 });
    setOpen(true);
  }

  function openEdit(s: ServiceRow) {
    setEditing(s);
    setForm({
      slug: s.slug,
      category: s.category,
      nameFi: s.name.values.fi ?? '', nameEn: s.name.values.en ?? '', nameSv: s.name.values.sv ?? '',
      descriptionFi: s.description.values.fi ?? '', descriptionEn: s.description.values.en ?? '', descriptionSv: s.description.values.sv ?? '',
      additionalFi: s.additionalInfo?.values.fi ?? '', additionalEn: s.additionalInfo?.values.en ?? '', additionalSv: s.additionalInfo?.values.sv ?? '',
      priceNet: s.priceNet,
      vatRatePercent: s.vatRatePercent,
      icon: s.icon,
      imageUrl: s.imageUrl,
      isFeatured: s.isFeatured,
      sortOrder: s.sortOrder,
    });
    setOpen(true);
  }

  function fields() {
    return {
      slug: form.slug,
      category: form.category,
      name: { fi: form.nameFi, en: form.nameEn, sv: form.nameSv },
      description: { fi: form.descriptionFi, en: form.descriptionEn, sv: form.descriptionSv },
      additionalInfo: { fi: form.additionalFi, en: form.additionalEn, sv: form.additionalSv },
      durationMinutes: 120,
      priceNet: Number(form.priceNet),
      vatRatePercent: Number(form.vatRatePercent),
      currency: 'EUR',
      isFeatured: form.isFeatured,
      sortOrder: Number(form.sortOrder),
      icon: form.icon,
      imageUrl: form.imageUrl,
    };
  }

  async function save() {
    if (editing) {
      await adminServices.update(editing.id, fields(), editing.isActive);
    } else {
      await adminServices.create(fields());
    }
    setOpen(false);
    load();
  }

  async function toggleActive(s: ServiceRow) {
    await adminServices.update(s.id, fieldsForRow(s), !s.isActive);
    load();
  }

  function fieldsForRow(s: ServiceRow) {
    return {
      slug: s.slug,
      category: s.category,
      name: s.name.values,
      description: s.description.values,
      additionalInfo: s.additionalInfo?.values ?? {},
      durationMinutes: s.durationMinutes,
      priceNet: s.priceNet,
      vatRatePercent: s.vatRatePercent,
      currency: 'EUR',
      isFeatured: s.isFeatured,
      sortOrder: s.sortOrder,
      icon: s.icon,
      imageUrl: s.imageUrl,
    };
  }

  async function move(s: ServiceRow, dir: -1 | 1) {
    const sorted = [...(services ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
    const idx = sorted.findIndex((x) => x.id === s.id);
    const swap = sorted[idx + dir];
    if (!swap) return;
    await adminServices.update(s.id, { ...fieldsForRow(s), sortOrder: swap.sortOrder }, s.isActive);
    await adminServices.update(swap.id, { ...fieldsForRow(swap), sortOrder: s.sortOrder }, swap.isActive);
    load();
  }

  async function uploadImage() {
    const file = fileRef.current?.files?.[0];
    if (!file || !editing) return;
    setUploading(true);
    const url = await adminServices.uploadImage(editing.id, file);
    if (url) setForm((f) => ({ ...f, imageUrl: url }));
    setUploading(false);
  }

  const set = (key: keyof FormState) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Services</h1>
          <p className="text-sm text-muted-foreground">
            First four active services in order are shown on the website.
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" />
          Add service
        </Button>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{editing ? 'Edit service' : 'New service'}</DialogTitle>
              <DialogDescription>Changes appear on the website immediately.</DialogDescription>
            </DialogHeader>
            <div className="grid gap-4 py-2 sm:grid-cols-3">
              <Field label="Slug" value={form.slug} onChange={set('slug')} />
              <Field label="Category" value={form.category} onChange={set('category')} />
              <Field label="Price net (€)" value={String(form.priceNet)} onChange={set('priceNet')} />
              <Field label="Name (fi)" value={form.nameFi} onChange={set('nameFi')} />
              <Field label="Name (en)" value={form.nameEn} onChange={set('nameEn')} />
              <Field label="Name (sv)" value={form.nameSv} onChange={set('nameSv')} />
              <Field label="Description (fi)" value={form.descriptionFi} onChange={set('descriptionFi')} />
              <Field label="Description (en)" value={form.descriptionEn} onChange={set('descriptionEn')} />
              <Field label="Description (sv)" value={form.descriptionSv} onChange={set('descriptionSv')} />
              <Field label="Additional info (fi)" value={form.additionalFi} onChange={set('additionalFi')} />
              <Field label="Additional info (en)" value={form.additionalEn} onChange={set('additionalEn')} />
              <Field label="Additional info (sv)" value={form.additionalSv} onChange={set('additionalSv')} />
              <Field label="VAT %" value={String(form.vatRatePercent)} onChange={set('vatRatePercent')} />

              <div className="space-y-1.5">
                <Label className="text-xs">Icon</Label>
                <div className="flex flex-wrap gap-1.5 rounded-md border p-2">
                  {Object.keys(SERVICE_ICONS).map((name) => {
                    const IconCmp = SERVICE_ICONS[name];
                    return (
                      <button
                        key={name}
                        type="button"
                        title={name}
                        onClick={() => setForm((f) => ({ ...f, icon: name }))}
                        className={
                          'rounded-md border p-1.5 transition-colors ' +
                          (form.icon === name
                            ? 'border-primary bg-primary text-primary-foreground'
                            : 'hover:bg-muted')
                        }
                      >
                        <IconCmp className="h-4 w-4" />
                      </button>
                    );
                  })}
                </div>
              </div>

              {editing && (
                <div className="space-y-1.5 sm:col-span-3">
                  <Label className="text-xs">Card image</Label>
                  <div className="flex items-center gap-3">
                    <input ref={fileRef} type="file" accept=".jpg,.jpeg,.png,.webp" className="text-xs" />
                    <Button type="button" variant="outline" size="sm" onClick={uploadImage} disabled={uploading}>
                      <Upload className="mr-1.5 h-3.5 w-3.5" />
                      {uploading ? 'Uploading…' : 'Upload'}
                    </Button>
                    {form.imageUrl && (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img src={form.imageUrl} alt="" className="h-10 w-16 rounded-md border object-cover" />
                    )}
                  </div>
                </div>
              )}
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
              <Button onClick={save}>{editing ? 'Save' : 'Create'}</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>

      <div className="rounded-xl border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Order</TableHead>
              <TableHead>Service</TableHead>
              <TableHead>Slug</TableHead>
              <TableHead>Price net</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {services?.map((s) => {
              const IconCmp = serviceIcon(s.icon);
              return (
                <TableRow key={s.id} className={!s.isActive ? 'opacity-60' : undefined}>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      <Button variant="ghost" size="icon" className="h-7 w-7" onClick={() => move(s, -1)}>
                        <ArrowUp className="h-3.5 w-3.5" />
                      </Button>
                      <Button variant="ghost" size="icon" className="h-7 w-7" onClick={() => move(s, 1)}>
                        <ArrowDown className="h-3.5 w-3.5" />
                      </Button>
                      <span className="ml-1 text-xs text-muted-foreground">{s.sortOrder + 1}</span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-3">
                      <span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary text-primary-foreground">
                        <IconCmp className="h-4 w-4" />
                      </span>
                      <div>
                        <p className="font-medium">{s.name.values.fi}</p>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{s.slug}</TableCell>
                  <TableCell>{s.priceNet} €</TableCell>
                  <TableCell>
                    {s.isActive
                      ? <Badge variant="default">Active</Badge>
                      : <Badge variant="secondary">Inactive</Badge>}
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex items-center justify-end gap-3">
                      <Switch checked={s.isActive} onCheckedChange={() => toggleActive(s)} />
                      <Button variant="ghost" size="icon" onClick={() => openEdit(s)}>
                        <Pencil className="h-4 w-4" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
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
