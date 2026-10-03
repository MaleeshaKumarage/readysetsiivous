'use client';

import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { adminShifts, Shift } from '@/lib/adminApi';

export default function ShiftsAdminPage({ params }: { params: { lang: string } }) {
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    adminShifts.list().then((result) => {
      if (active && result) setShifts(result);
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, []);

  return (
    <div>
      <h1 className="mb-4 text-2xl font-semibold">Shifts</h1>
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
                <p>{shift.companyId} · {shift.branchId}</p>
                <p>{shift.schedule.type}</p>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
