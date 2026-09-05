'use client';

import AdminShell from '@/components/admin/AdminShell';
import Navbar from '@/components/Navbar';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import EmployeesAdmin from '@/components/admin/EmployeesAdmin';

export default function AdminEmployeesPage({ params }: { params: { lang: string } }) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;

  return (
    <>
      <Navbar lang={lang} />
      <div className="pt-16">
        <AdminShell lang={params.lang} active="employees">
      <EmployeesAdmin />
        </AdminShell>
      </div>
    </>
  );
}
