'use client';

import { useEffect, useState } from 'react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { adminCompanies, adminBranches, type Company, type Branch } from '@/lib/adminApi';

const emptyForm = {
  businessId: '',
  name: '',
  contactName: '',
  contactEmail: '',
  contactPhone: '',
  notes: '',
};

const emptyBranch = { name: '', street: '', postalCode: '', city: '', country: '', contactPhone: '' };

export default function CompaniesAdminPage({ params }: { params: { lang: string } }) {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const [expanded, setExpanded] = useState<string | null>(null);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [branchForm, setBranchForm] = useState(emptyBranch);
  const [branchSaving, setBranchSaving] = useState(false);
  const [branchError, setBranchError] = useState('');

  const refresh = () =>
    adminCompanies.list(debouncedSearch, 0, 100).then((result) => {
      if (result) setCompanies(result.items);
      setLoading(false);
    });

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(handle);
  }, [search]);

  useEffect(() => {
    let active = true;
    setLoading(true);
    adminCompanies.list(debouncedSearch, 0, 100).then((result) => {
      if (active && result) setCompanies(result.items);
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [debouncedSearch]);

  const update = (key: keyof typeof emptyForm) => (
    e: React.ChangeEvent<HTMLInputElement>
  ) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const updateBranch = (key: keyof typeof emptyBranch) => (
    e: React.ChangeEvent<HTMLInputElement>
  ) => setBranchForm((f) => ({ ...f, [key]: e.target.value }));

  const submit = async () => {
    if (!form.businessId.trim() || !form.name.trim()) {
      setError('Business ID and name are required.');
      return;
    }
    setSaving(true);
    setError('');
    const created = await adminCompanies.create({
      businessId: form.businessId.trim(),
      name: form.name.trim(),
      contactName: form.contactName.trim() || undefined,
      contactEmail: form.contactEmail.trim() || undefined,
      contactPhone: form.contactPhone.trim() || undefined,
      notes: form.notes.trim() || undefined,
    });
    setSaving(false);
    if (!created) {
      setError('Failed to create company.');
      return;
    }
    setForm(emptyForm);
    setShowForm(false);
    await refresh();
  };

  const toggleBranches = async (id: string) => {
    if (expanded === id) { setExpanded(null); return; }
    setExpanded(id);
    const detail = await adminCompanies.get(id);
    if (detail) setBranches(detail.branches);
  };

  const submitBranch = async () => {
    if (!expanded || !branchForm.name.trim()) {
      setBranchError('Branch name is required.');
      return;
    }
    setBranchSaving(true);
    setBranchError('');
    const created = await adminBranches.create(expanded, {
      name: branchForm.name.trim(),
      street: branchForm.street.trim() || undefined,
      postalCode: branchForm.postalCode.trim() || undefined,
      city: branchForm.city.trim() || undefined,
      country: branchForm.country.trim() || undefined,
      contactPhone: branchForm.contactPhone.trim() || undefined,
    });
    setBranchSaving(false);
    if (!created) {
      setBranchError('Failed to create branch.');
      return;
    }
    setBranchForm(emptyBranch);
    const detail = await adminCompanies.get(expanded);
    if (detail) setBranches(detail.branches);
  };

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Companies</h1>
        <Button onClick={() => setShowForm((v) => !v)}>
          {showForm ? 'Cancel' : 'Add company'}
        </Button>
      </div>

      {showForm && (
        <Card className="mb-6">
          <CardHeader>
            <CardTitle>New company</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-2">
            <div className="grid gap-1">
              <Label htmlFor="businessId">Business ID</Label>
              <Input id="businessId" value={form.businessId} onChange={update('businessId')} />
            </div>
            <div className="grid gap-1">
              <Label htmlFor="name">Name</Label>
              <Input id="name" value={form.name} onChange={update('name')} />
            </div>
            <div className="grid gap-1">
              <Label htmlFor="contactName">Contact name</Label>
              <Input id="contactName" value={form.contactName} onChange={update('contactName')} />
            </div>
            <div className="grid gap-1">
              <Label htmlFor="contactEmail">Contact email</Label>
              <Input id="contactEmail" value={form.contactEmail} onChange={update('contactEmail')} />
            </div>
            <div className="grid gap-1">
              <Label htmlFor="contactPhone">Contact phone</Label>
              <Input id="contactPhone" value={form.contactPhone} onChange={update('contactPhone')} />
            </div>
            <div className="grid gap-1">
              <Label htmlFor="notes">Notes</Label>
              <Input id="notes" value={form.notes} onChange={update('notes')} />
            </div>
            <div className="sm:col-span-2">
              {error && <p className="mb-2 text-sm text-destructive">{error}</p>}
              <Button onClick={submit} disabled={saving}>
                {saving ? 'Saving…' : 'Create company'}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <Input
        placeholder="Search business ID or name"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="mb-4 max-w-sm"
      />
      {loading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2">
          {companies.map((company) => (
            <Card key={company.id}>
              <CardHeader>
                <CardTitle>{company.name}</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-muted-foreground">{company.businessId}</p>
                <Button variant="outline" size="sm" className="mt-2" onClick={() => toggleBranches(company.id)}>
                  {expanded === company.id ? 'Hide branches' : `Branches (${branches.length && expanded === company.id ? branches.length : ''})`}
                </Button>
                {expanded === company.id && (
                  <div className="mt-3 space-y-3">
                    {branches.length === 0 ? (
                      <p className="text-sm text-muted-foreground">No branches yet.</p>
                    ) : (
                      branches.map((b) => (
                        <div key={b.id} className="rounded border p-2 text-sm">
                          <span className="font-medium">{b.name}</span>
                          {b.city && <span className="text-muted-foreground"> · {b.city}</span>}
                        </div>
                      ))
                    )}
                    <div className="grid gap-2 rounded border p-3">
                      <p className="text-sm font-medium">Add branch</p>
                      <Input placeholder="Name" value={branchForm.name} onChange={updateBranch('name')} />
                      <Input placeholder="Street" value={branchForm.street} onChange={updateBranch('street')} />
                      <div className="flex gap-2">
                        <Input placeholder="Postal code" value={branchForm.postalCode} onChange={updateBranch('postalCode')} />
                        <Input placeholder="City" value={branchForm.city} onChange={updateBranch('city')} />
                      </div>
                      <Input placeholder="Country" value={branchForm.country} onChange={updateBranch('country')} />
                      <Input placeholder="Phone" value={branchForm.contactPhone} onChange={updateBranch('contactPhone')} />
                      {branchError && <p className="text-sm text-destructive">{branchError}</p>}
                      <Button size="sm" onClick={submitBranch} disabled={branchSaving}>
                        {branchSaving ? 'Saving…' : 'Add branch'}
                      </Button>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
