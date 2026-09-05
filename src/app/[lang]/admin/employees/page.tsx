'use client';

import AdminShell from '@/components/admin/AdminShell';
import EmployeesAdmin from '@/components/admin/EmployeesAdmin';

export default function AdminEmployeesPage({ params }: { params: { lang: string } }) {
  return (
    <AdminShell lang={params.lang} active="employees">
      <EmployeesAdmin />
    </AdminShell>
  );
}
