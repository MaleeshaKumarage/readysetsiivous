'use client';

import Link from 'next/link';
import { Sparkles, FileSignature } from 'lucide-react';
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

const SECTIONS = [
  { href: 'services', title: 'Services', description: 'Manage cleaning services, prices and descriptions.', icon: Sparkles },
  { href: 'agreements', title: 'Agreements', description: 'Upload agreements and collect e-signatures.', icon: FileSignature },
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
