'use client';

import AdminShell from '@/components/admin/AdminShell';
import SettingsAdmin from '@/components/admin/SettingsAdmin';

export default function AdminSettingsPage({ params }: { params: { lang: string } }) {
  return (
    <AdminShell lang={params.lang} active="settings">
      <SettingsAdmin />
    </AdminShell>
  );
}
