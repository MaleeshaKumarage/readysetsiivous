'use client';

import { useEffect, useState } from 'react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { adminCompanies, Company } from '@/lib/adminApi';

const emptyForm = {
  businessId: '',
  name: '',
  contactName: '',
  contactEmail: '',
  contactPhone: '',
  notes: '',
};

export default function CompaniesAdminPage({ params }: { params: { lang: string } }) {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

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
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
