'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { initAuth, isAuthenticated, isAdmin, login } from '@/lib/auth';

export default function AdminIndexPage({ params }: { params: { lang: string } }) {
  const router = useRouter();

  useEffect(() => {
    initAuth().then(() => {
      if (isAuthenticated() && isAdmin()) {
        router.replace(`/${params.lang}/admin/services/`);
      } else {
        login();
      }
    });
  }, [params.lang, router]);

  return <p className="p-8 text-center text-muted-foreground">Redirecting…</p>;
}
