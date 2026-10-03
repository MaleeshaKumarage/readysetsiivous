'use client';

import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { adminShifts, adminCompanies, adminBranches, type Shift, type Company, type Branch } from '@/lib/adminApi';

export default function ShiftsAdminPage({ params }: { params: { lang: string } }) {
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [loading, setLoading] = useState(true);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [showForm, setShowForm] = useState(false);

  const [companyId, setCompanyId] = useState('');
  const [branchId, setBranchId] = useState('');
  const [name, setName] = useState('');
  const [dailyStart, setDailyStart] = useState('08:00');
  const [dailyEnd, setDailyEnd] = useState('17:00');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = () =>
    adminShifts.list().then((result) => {
      if (result) setShifts(result);
      setLoading(false);
    });

  useEffect(() => {
    load();
    adminCompanies.list('', 0, 100).then((r) => { if (r) setCompanies(r.items); });
  }, []);

  useEffect(() => {
    if (!companyId) { setBranches([]); return; }
    adminCompanies.get(companyId).then((d) => { if (d) setBranches(d.branches); });
  }, [companyId]);

  const submit = async () => {
    if (!companyId || !branchId || !name.trim()) {
      setError('Company, branch and name are required.');
      return;
    }
    setSaving(true);
    setError('');
    const created = await adminShifts.create({
      companyId,
      branchId,
      name: name.trim(),
      schedule: { type: 0, dailyStart, dailyEnd },
      notes: notes.trim() || undefined,
    });
    setSaving(false);
    if (!created) {
      setError('Failed to create shift.');
      return;
    }
    setName(''); setNotes(''); setShowForm(false);
    await load();
  };

  const deactivate = async (id: string) => {
    await adminShifts.deactivate(id);
    await load();
  };

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Shifts</h1>
        <Button onClick={() => setShowForm((v) => !v)}>
          {showForm ? 'Cancel' : 'Add shift'}
        </Button>
      </div>

      {showForm && (
        <Card className="mb-6">
          <CardHeader><CardTitle>New shift</CardTitle></CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-2">
            <div className="grid gap-1">
              <Label>Company</Label>
              <select value={companyId} onChange={(e) => { setCompanyId(e.target.value); setBranchId(''); }}
                className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm">
                <option value="">Select company…</option>
                {companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </div>
            <div className="grid gap-1">
              <Label>Branch</Label>
              <select value={branchId} onChange={(e) => setBranchId(e.target.value)}
                className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm">
                <option value="">Select branch…</option>
                {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
              </select>
            </div>
            <div className="grid gap-1">
              <Label>Name</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <div className="grid gap-1 sm:grid-cols-2">
              <div className="grid gap-1">
                <Label>Start</Label>
                <Input type="time" value={dailyStart} onChange={(e) => setDailyStart(e.target.value)} />
              </div>
              <div className="grid gap-1">
                <Label>End</Label>
                <Input type="time" value={dailyEnd} onChange={(e) => setDailyEnd(e.target.value)} />
              </div>
            </div>
            <div className="grid gap-1 sm:col-span-2">
              <Label>Notes</Label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <div className="sm:col-span-2">
              {error && <p className="mb-2 text-sm text-destructive">{error}</p>}
              <Button onClick={submit} disabled={saving}>
                {saving ? 'Saving…' : 'Create shift'}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2">
          {shifts.map((shift) => (
            <Card key={shift.id}>
              <CardHeader>
                <CardTitle>{shift.name}</CardTitle>
              </CardHeader>
              <CardContent className="text-sm text-muted-foreground">
                <p>{shift.schedule.type === 0 ? `${shift.schedule.dailyStart ?? ''} – ${shift.schedule.dailyEnd ?? ''}` : `type ${shift.schedule.type}`}</p>
                <div className="mt-2 flex items-center gap-2">
                  {shift.isActive && (
                    <Button variant="outline" size="sm" onClick={() => deactivate(shift.id)}>Deactivate</Button>
                  )}
                  {!shift.isActive && <Badge>Inactive</Badge>}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
