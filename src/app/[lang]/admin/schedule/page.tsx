'use client';

import AdminShell from '@/components/admin/AdminShell';
import ScheduleAdmin from '@/components/admin/ScheduleAdmin';

export default function AdminSchedulePage({ params }: { params: { lang: string } }) {
  return (
    <AdminShell lang={params.lang} active="schedule">
      <ScheduleAdmin />
    </AdminShell>
  );
}
