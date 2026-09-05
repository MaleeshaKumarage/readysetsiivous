'use client';

import Link from 'next/link';
import { Sparkles, CalendarDays, CalendarRange, FileText, Users, Settings } from 'lucide-react';
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

const SECTIONS = [
  { href: 'services', title: 'Services', description: 'Manage cleaning services, prices and descriptions.', icon: Sparkles },
  { href: 'bookings', title: 'Bookings', description: 'Confirm, complete and assign customer bookings.', icon: CalendarDays },
  { href: 'schedule', title: 'Schedule', description: 'Day view per employee.', icon: CalendarRange },
  { href: 'invoices', title: 'Invoices', description: 'Invoice completed bookings, download PDFs.', icon: FileText },
  { href: 'employees', title: 'Employees', description: 'Staff, roles and Keycloak invites.', icon: Users },
  { href: 'settings', title: 'Settings', description: 'Company details and public page content.', icon: Settings },
] as const;

export default function AdminDashboardPage({ params }: { params: { lang: string } }) {
  const base = `/${params.lang}/admin/`;

  return (
    <div>
      <h1 className="mb-6 text-2xl font-semibold">Dashboard</h1>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {SECTIONS.map((s) => (
          <Link key={s.href} href={base + s.href + '/'}>
            <Card className="transition-colors hover:border-primary/50 hover:bg-accent/50">
              <CardHeader>
                <s.icon className="mb-2 h-5 w-5 text-primary" />
                <CardTitle className="text-base">{s.title}</CardTitle>
                <CardDescription>{s.description}</CardDescription>
              </CardHeader>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
