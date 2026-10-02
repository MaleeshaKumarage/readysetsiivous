'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Plus, Trash2, Pencil, Send } from 'lucide-react';
import { adminEmployees, type Employee } from '@/lib/adminApi';
import { isAdmin } from '@/lib/auth';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;

type Hours = Record<string, { start: string; end: string }>;

interface FormState {
  email: string; firstName: string; lastName: string; phone: string; role: string; colorHex: string;
  skills: string; serviceAreas: string; payRate: string; notes: string;
  certifications: { name: string; expiresAt: string }[];
  hours: Hours;
}

function emptyForm(): FormState {
  return {
    email: '', firstName: '', lastName: '', phone: '', role: 'employee', colorHex: '',
    skills: '', serviceAreas: '', payRate: '', notes: '', certifications: [],
    hours: Object.fromEntries(DAYS.map((d) => [d, { start: '', end: '' }])),
  };
}

function toForm(e: Employee): FormState {
  const hours: Hours = {};
  for (const d of DAYS) {
    const h = e.defaultHours?.[d];
    hours[d] = { start: h?.start ? h.start.slice(0, 5) : '', end: h?.end ? h.end.slice(0, 5) : '' };
  }
  return {
    email: e.email, firstName: e.firstName, lastName: e.lastName, phone: e.phone,
    role: e.role, colorHex: e.colorHex ?? '',
    skills: (e.skills ?? []).join(', '), serviceAreas: (e.serviceAreas ?? []).join(', '),
    payRate: e.payRate != null ? String(e.payRate) : '', notes: e.notes ?? '',
    certifications: (e.certifications ?? []).map((c) => ({ name: c.name, expiresAt: c.expiresAtUtc ? c.expiresAtUtc.slice(0, 10) : '' })),
    hours,
  };
}

function buildFields(f: FormState) {
  const toTime = (s: string) => (s ? s + ':00' : null);
  const defaultHours: Record<string, { start: string | null; end: string | null }> = {};
  for (const d of DAYS) defaultHours[d] = { start: toTime(f.hours[d].start), end: toTime(f.hours[d].end) };
  return {
    email: f.email, firstName: f.firstName, lastName: f.lastName, phone: f.phone,
    role: f.role, colorHex: f.colorHex || null,
    skills: f.skills.split(',').map((s) => s.trim()).filter(Boolean),
    serviceAreas: f.serviceAreas.split(',').map((s) => s.trim()).filter(Boolean),
    payRate: f.payRate ? Number(f.payRate) : null,
    certifications: f.certifications.filter((c) => c.name.trim()).map((c) => ({ name: c.name.trim(), expiresAtUtc: c.expiresAt ? new Date(c.expiresAt).toISOString() : null })),
    notes: f.notes || null,
    defaultHours,
  };
}

function certStatus(e: Employee): 'expired' | 'expiring' | null {
  const now = Date.now();
  let expiring = false;
  for (const c of e.certifications ?? []) {
    if (!c.expiresAtUtc) continue;
    const t = new Date(c.expiresAtUtc).getTime();
    if (t < now) return 'expired';
    if (t - now < 30 * 24 * 3600 * 1000) expiring = true;
  }
  return expiring ? 'expiring' : null;
}

export default function EmployeesPage() {
  const [items, setItems] = useState<Employee[] | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Employee | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm());
  const [tab, setTab] = useState<'profile' | 'availability' | 'skills' | 'certs' | 'notes'>('profile');
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => { setItems(await adminEmployees.list()); }, []);
  useEffect(() => { load(); }, [load]);

  function openNew() { setEditing(null); setForm(emptyForm()); setTab('profile'); setOpen(true); }
  function openEdit(e: Employee) { setEditing(e); setForm(toForm(e)); setTab('profile'); setOpen(true); }

  function set<K extends keyof FormState>(k: K, v: FormState[K]) { setForm((f) => ({ ...f, [k]: v })); }

  async function save() {
    if (!form.firstName || !form.lastName || !form.email || submitting) return;
    setSubmitting(true);
    try {
      const fields = buildFields(form);
      if (editing) await adminEmployees.update(editing.id, fields, editing.isActive);
      else await adminEmployees.create(fields);
      setOpen(false);
      load();
      toast.success(editing ? 'Employee updated' : 'Employee created');
    } finally {
      setSubmitting(false);
    }
  }

  async function invite(e: Employee) {
    const r = await adminEmployees.invite(e.id);
    if (r) toast.success(`Invitation sent to ${r.email}`);
  }

  async function toggle(e: Employee) {
    if (e.isActive) await adminEmployees.deactivate(e.id);
    else await adminEmployees.update(e.id, buildFields(toForm(e)), true);
    load();
  }

  const tabBtn = (t: typeof tab, label: string) => (
    <Button size="sm" onClick={() => setTab(t)}
      className={tab === t ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-muted/80'}>
      {label}
    </Button>
  );

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Employees</h1>
        {isAdmin() && <Button onClick={openNew}><Plus className="mr-1.5 h-4 w-4" />New employee</Button>}
      </div>

      <div className="rounded-xl border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead><TableHead>Email</TableHead><TableHead>Role</TableHead>
              <TableHead>Phone</TableHead><TableHead>Certs</TableHead><TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items?.map((e) => {
              const cs = certStatus(e);
              return (
                <TableRow key={e.id}>
                  <TableCell className="font-medium">
                    {e.firstName} {e.lastName}
                    {!e.isActive && <Badge variant="secondary" className="ml-2">Inactive</Badge>}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{e.email}</TableCell>
                  <TableCell>{e.role}</TableCell>
                  <TableCell className="text-muted-foreground">{e.phone}</TableCell>
                  <TableCell>
                    {cs === 'expired' && <Badge className="bg-destructive text-destructive-foreground">Expired</Badge>}
                    {cs === 'expiring' && <Badge className="bg-amber-500 text-black">Expiring</Badge>}
                  </TableCell>
                  <TableCell className="text-right">
                    {isAdmin() && (
                      <>
                        <Button variant="ghost" size="sm" onClick={() => openEdit(e)}><Pencil className="mr-1 h-3.5 w-3.5" />Edit</Button>
                        <Button variant="ghost" size="sm" onClick={() => invite(e)}><Send className="mr-1 h-3.5 w-3.5" />Invite</Button>
                        <Button variant="ghost" size="sm" onClick={() => toggle(e)}>{e.isActive ? 'Deactivate' : 'Activate'}</Button>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-xl">
          <DialogHeader><DialogTitle>{editing ? 'Edit employee' : 'New employee'}</DialogTitle></DialogHeader>
          <div className="flex flex-wrap gap-2">
            {tabBtn('profile', 'Profile')}
            {tabBtn('availability', 'Availability')}
            {tabBtn('skills', 'Skills & Areas')}
            {tabBtn('certs', 'Certifications')}
            {tabBtn('notes', 'Notes')}
          </div>

          {tab === 'profile' && (
            <div className="grid gap-3">
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1.5"><Label>First name</Label><Input value={form.firstName} onChange={(e) => set('firstName', e.target.value)} /></div>
                <div className="space-y-1.5"><Label>Last name</Label><Input value={form.lastName} onChange={(e) => set('lastName', e.target.value)} /></div>
              </div>
              <div className="space-y-1.5"><Label>Email</Label><Input value={form.email} onChange={(e) => set('email', e.target.value)} /></div>
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1.5"><Label>Phone</Label><Input value={form.phone} onChange={(e) => set('phone', e.target.value)} /></div>
                <div className="space-y-1.5">
                  <Label>Role</Label>
                  <select value={form.role} onChange={(e) => set('role', e.target.value)}
                    className="flex h-9 w-full rounded-md border border-input bg-background px-3 text-sm">
                    <option value="employee">Employee</option>
                    <option value="admin">Admin</option>
                  </select>
                </div>
              </div>
              <div className="space-y-1.5"><Label>Color</Label><Input type="color" value={form.colorHex || '#000000'} onChange={(e) => set('colorHex', e.target.value)} className="h-10 w-20 p-1" /></div>
            </div>
          )}

          {tab === 'availability' && (
            <div className="grid gap-2">
              {DAYS.map((d) => (
                <div key={d} className="grid grid-cols-[6rem_1fr_1fr] items-center gap-2">
                  <span className="text-sm">{d}</span>
                  <Input type="time" value={form.hours[d].start} onChange={(e) => set('hours', { ...form.hours, [d]: { ...form.hours[d], start: e.target.value } })} />
                  <Input type="time" value={form.hours[d].end} onChange={(e) => set('hours', { ...form.hours, [d]: { ...form.hours[d], end: e.target.value } })} />
                </div>
              ))}
              <p className="text-xs text-muted-foreground">Leave both blank = day off.</p>
            </div>
          )}

          {tab === 'skills' && (
            <div className="grid gap-3">
              <div className="space-y-1.5"><Label>Skills (comma-separated)</Label><Input value={form.skills} onChange={(e) => set('skills', e.target.value)} placeholder="Deep clean, Office, Move-out" /></div>
              <div className="space-y-1.5"><Label>Service areas (comma-separated)</Label><Input value={form.serviceAreas} onChange={(e) => set('serviceAreas', e.target.value)} placeholder="Helsinki, Espoo, Vantaa" /></div>
              <div className="space-y-1.5"><Label>Pay rate (€/hour)</Label><Input type="number" min="0" step="0.01" value={form.payRate} onChange={(e) => set('payRate', e.target.value)} /></div>
            </div>
          )}

          {tab === 'certs' && (
            <div className="grid gap-2">
              {form.certifications.map((c, i) => (
                <div key={i} className="flex gap-2">
                  <Input placeholder="Certification name" value={c.name} onChange={(e) => {
                    const n = [...form.certifications]; n[i].name = e.target.value; set('certifications', n);
                  }} />
                  <Input type="date" value={c.expiresAt} onChange={(e) => {
                    const n = [...form.certifications]; n[i].expiresAt = e.target.value; set('certifications', n);
                  }} />
                  <Button variant="ghost" size="icon" onClick={() => set('certifications', form.certifications.filter((_, j) => j !== i))}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button variant="outline" size="sm" onClick={() => set('certifications', [...form.certifications, { name: '', expiresAt: '' }])}>
                <Plus className="mr-1.5 h-3.5 w-3.5" />Add certification
              </Button>
            </div>
          )}

          {tab === 'notes' && (
            <div className="space-y-1.5">
              <Label>Notes</Label>
              <textarea value={form.notes} onChange={(e) => set('notes', e.target.value)}
                className="min-h-24 w-full rounded-md border border-input bg-background px-3 py-2 text-sm" />
            </div>
          )}

          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
            <Button onClick={save} disabled={submitting}>{submitting ? 'Saving…' : 'Save'}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
