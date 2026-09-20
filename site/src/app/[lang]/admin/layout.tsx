'use client';

import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { LayoutDashboard, Sparkles, FileSignature, LogOut, LogIn } from 'lucide-react';
import { initAuth, isAuthenticated, login, logout } from '@/lib/auth';
import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';
import { cn } from '@/lib/utils';

const NAV = [
  { href: 'services', label: 'Services', icon: Sparkles },
  { href: 'agreements', label: 'Agreements', icon: FileSignature },
] as const;

export default function AdminLayout({ params, children }: { params: { lang: string }; children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const pathname = usePathname() ?? '';

  useEffect(() => {
    initAuth().then(() => {
      setAuthed(isAuthenticated());
      setReady(true);
    });
  }, []);

  // Admin panel is always dark mode. Force the class on, beating the ThemeProvider's
  // mount effect (children run before the parent provider), and restore the user's
  // stored theme when leaving admin.
  useEffect(() => {
    const root = document.documentElement;
    const restore = () => {
      let stored: string | null = null;
      try { stored = localStorage.getItem('theme'); } catch { /* ignore */ }
      if (stored === 'dark') root.classList.add('dark');
      else root.classList.remove('dark');
    };

    root.classList.add('dark');
    const t = setTimeout(() => root.classList.add('dark'), 0);

    return () => { clearTimeout(t); restore(); };
  }, []);

  if (!ready) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-sm text-muted-foreground">Checking login…</p>
      </div>
    );
  }

  if (!authed) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-muted/40">
        <div className="w-full max-w-sm rounded-xl border bg-card p-8 text-center shadow-sm">
          <LayoutDashboard className="mx-auto mb-4 h-10 w-10 text-primary" />
          <h1 className="mb-2 text-xl font-semibold">Admin</h1>
          <p className="mb-6 text-sm text-muted-foreground">
            Sign in with your ReadySetSiivous admin account.
          </p>
          <Button className="w-full" onClick={() => login()}>
            <LogIn className="mr-2 h-4 w-4" />
            Sign in
          </Button>
        </div>
      </div>
    );
  }

  const base = `/${params.lang}/admin/`;

  return (
    <div className="flex min-h-screen">
      <aside className="fixed inset-y-0 left-0 z-40 flex w-60 flex-col border-r bg-background">
        <div className="flex h-14 items-center gap-2 border-b px-4">
          <Link href={base} className="flex items-center gap-2 font-semibold">
            <span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary text-primary-foreground">
              <LayoutDashboard className="h-4 w-4" />
            </span>
            ReadySet<span className="text-primary">Siivous</span>
          </Link>
        </div>
        <nav className="flex-1 space-y-1 overflow-y-auto p-3">
          {NAV.map((item) => {
            const active = pathname.startsWith(base + item.href);
            return (
              <Link
                key={item.href}
                href={base + item.href + '/'}
                className={cn(
                  'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                  active
                    ? 'bg-primary text-primary-foreground'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground'
                )}
              >
                <item.icon className="h-4 w-4" />
                {item.label}
              </Link>
            );
          })}
        </nav>
        <Separator />
        <div className="p-3">
          <Button variant="ghost" className="w-full justify-start text-muted-foreground" onClick={() => logout()}>
            <LogOut className="mr-3 h-4 w-4" />
            Sign out
          </Button>
        </div>
      </aside>
      <main className="ml-60 flex-1 p-8">{children}</main>
    </div>
  );
}
