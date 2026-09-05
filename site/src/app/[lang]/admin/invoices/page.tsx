'use client';

import AdminShell from '@/components/admin/AdminShell';
import InvoicesAdmin from '@/components/admin/InvoicesAdmin';

export default function AdminInvoicesPage({ params }: { params: { lang: string } }) {
  return (
    <AdminShell lang={params.lang} active="invoices">
      <InvoicesAdmin />
    </AdminShell>
  );
}
