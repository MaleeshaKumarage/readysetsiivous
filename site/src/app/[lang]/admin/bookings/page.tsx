'use client';

import AdminShell from '@/components/admin/AdminShell';
import Navbar from '@/components/Navbar';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import BookingsAdmin from '@/components/admin/BookingsAdmin';

export default function AdminBookingsPage({ params }: { params: { lang: string } }) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;

  return (
    <>
      <Navbar lang={lang} />
      <div className="pt-16">
        <AdminShell lang={params.lang} active="bookings">
      <BookingsAdmin />
        </AdminShell>
      </div>
    </>
  );
}
