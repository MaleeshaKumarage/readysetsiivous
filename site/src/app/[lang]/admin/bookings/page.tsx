'use client';

import AdminShell from '@/components/admin/AdminShell';
import BookingsAdmin from '@/components/admin/BookingsAdmin';

export default function AdminBookingsPage({ params }: { params: { lang: string } }) {
  return (
    <AdminShell lang={params.lang} active="bookings">
      <BookingsAdmin />
    </AdminShell>
  );
}
