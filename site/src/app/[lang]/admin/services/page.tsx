'use client';

import AdminShell from '@/components/admin/AdminShell';
import ServicesAdmin from '@/components/admin/ServicesAdmin';

export default function AdminServicesPage({ params }: { params: { lang: string } }) {
  return (
    <AdminShell lang={params.lang} active="services">
      <ServicesAdmin />
    </AdminShell>
  );
}
