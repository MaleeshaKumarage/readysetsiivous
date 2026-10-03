'use client';

import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { adminCompanies, Company } from '@/lib/adminApi';

export default function CompaniesAdminPage({ params }: { params: { lang: string } }) {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    adminCompanies.list(search, 0, 100).then((result) => {
      if (active && result) setCompanies(result.items);
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [search]);

  return (
    <div>
      <h1 className="mb-4 text-2xl font-semibold">Companies</h1>
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
