'use client';

import '@mantine/core/styles.css';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import Link from 'next/link';
import Image from 'next/image';
import { usePathname } from 'next/navigation';
import {
  MantineProvider, AppShell, NavLink, Group, Text, Button, Stack, Paper, createTheme, Burger, ActionIcon, Tooltip,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  LayoutDashboard, Sparkles, FileSignature, Users, Building2, CalendarClock, CheckSquare, Receipt, LogOut, LogIn,
} from 'lucide-react';
import { initAuth, isAuthenticated, isAdmin, authTenant, refreshToken, login, logout } from '@/lib/auth';
import { API_URL } from '@/lib/api';
import { PwaInstallButton } from '@/components/PwaInstallButton';

const NAV = [
  { href: '', label: 'Dashboard', icon: LayoutDashboard },
  { href: 'services', label: 'Services', icon: Sparkles },
  { href: 'agreements', label: 'Agreements', icon: FileSignature },
  { href: 'employees', label: 'Employees', icon: Users },
  { href: 'companies', label: 'Companies', icon: Building2 },
  { href: 'shifts', label: 'Shifts', icon: CalendarClock },
  { href: 'invoices', label: 'Invoices', icon: Receipt },
  { href: 'quality-cycle', label: 'Quality Cycle', icon: CheckSquare },
] as const;

const adminTheme = createTheme({
  primaryColor: 'indigo',
  defaultRadius: 'md',
  fontFamily: 'Inter, system-ui, sans-serif',
});

export default function AdminLayout({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const [access, setAccess] = useState<'pending' | 'allowed' | 'denied' | 'error'>('pending');
  const [hasOwnWorkIdentity, setHasOwnWorkIdentity] = useState(false);
  const verification = useRef(0);
  const [opened, { toggle, close }] = useDisclosure(false);
  const pathname = usePathname() ?? '';

  const verifyAccess = useCallback(async () => {
    const attempt = ++verification.current;
    setAccess('pending');
    setHasOwnWorkIdentity(false);
    try {
      await initAuth();
      if (attempt !== verification.current) return;
      const signedIn = isAuthenticated();
      setAuthed(signedIn); setReady(true);
      if (!signedIn || !isAdmin()) { setAccess('denied'); return; }
      const bearer = await refreshToken();
      if (attempt !== verification.current) return;
      if (!bearer) { setAccess('denied'); return; }
      // Identity verification is deliberately uncached: stale roles and inactive
      // bindings must not mount administrative children or trigger their reads.
      const response = await fetch(`${API_URL}/api/v1/me`, { headers: { Authorization: `Bearer ${bearer}` }, cache: 'no-store' });
      if (attempt !== verification.current) return;
      if (response.status === 401 || response.status === 403) { setAccess('denied'); return; }
      if (!response.ok) throw new Error('Identity verification unavailable');
      const identity = await response.json();
      const hasAdmin = Array.isArray(identity.roles) && identity.roles.some((value: unknown) => {
        if (typeof value !== 'string') return false;
        try { const roles = JSON.parse(value)?.roles; return Array.isArray(roles) && roles.includes('admin'); } catch { return false; }
      });
      const allowed = identity.realm === authTenant() && hasAdmin
        && (!identity.employee || (identity.employee.role === 'admin' && identity.employee.isActive === true));
      if (attempt === verification.current) {
        setAccess(allowed ? 'allowed' : 'denied');
        setHasOwnWorkIdentity(allowed && identity.employee?.isActive === true);
      }
    } catch {
      if (attempt === verification.current) { setReady(true); setAccess('error'); }
    }
  }, []);

  useEffect(() => {
    void verifyAccess();
    return () => { verification.current++; };
  }, [verifyAccess]);

  // Keep the site's dark class for any pages still using shadcn components.
  useEffect(() => {
    const root = document.documentElement;
    root.classList.add('dark');
    root.setAttribute('data-mantine-color-scheme', 'dark');
    return () => {
      root.classList.remove('dark');
      root.removeAttribute('data-mantine-color-scheme');
    };
  }, []);

  const base = `/admin/`;

  if (!ready || (authed && access === 'pending')) {
    return (
      <MantineProvider theme={adminTheme} forceColorScheme="dark">
        <Group justify="center" h="100vh">
          <Text c="dimmed">Checking login…</Text>
        </Group>
      </MantineProvider>
    );
  }

  if (authed && access !== 'allowed') {
    return (
      <MantineProvider theme={adminTheme} forceColorScheme="dark">
        <Group justify="center" h="100vh" p="md">
          <Paper p="xl" radius="md" w="100%" maw={420} withBorder>
            <Stack>
              <Text component="h1" fw={600}>{access === 'error' ? 'Could not verify access' : 'Admin access required'}</Text>
              <Text role="alert">{access === 'error' ? 'Account verification is temporarily unavailable. Please retry.' : 'This account cannot access tenant administration.'}</Text>
              {access === 'error' && <Button onClick={() => void verifyAccess()}>Retry verification</Button>}
              <Button component={Link} href={`/account/?tenant=${encodeURIComponent(authTenant())}`} variant="light">My account</Button>
              <Button onClick={() => logout()} variant="subtle">Sign out</Button>
            </Stack>
          </Paper>
        </Group>
      </MantineProvider>
    );
  }

  if (!authed) {
    return (
      <MantineProvider theme={adminTheme} forceColorScheme="dark">
        <Group justify="center" h="100vh" p="md">
          <Paper p="xl" radius="md" w="100%" maw={380} withBorder>
            <Stack align="center">
              <LayoutDashboard size={40} />
              <Text fw={600} size="lg">Admin</Text>
              <Text c="dimmed" size="sm" ta="center">
                Sign in with your ReadySetSiivous admin account.
              </Text>
              <Button fullWidth leftSection={<LogIn size={16} />} onClick={() => login()}>
                Sign in
              </Button>
            </Stack>
          </Paper>
        </Group>
      </MantineProvider>
    );
  }

  return (
    <MantineProvider theme={adminTheme} forceColorScheme="dark">
      <AppShell
        header={{ height: 60 }}
        navbar={{
          width: 260,
          breakpoint: 'sm',
          collapsed: { mobile: !opened },
        }}
        padding="md"
      >
        <AppShell.Header p="sm">
          <Group h="100%" px="xs" justify="space-between" wrap="nowrap">
            <Group gap="xs">
              <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Toggle navigation" />
              <Link href="/admin/" onClick={close} style={{ textDecoration: 'none', color: 'inherit' }}>
                <Group gap="xs">
                  <Image src="/brand/readyset-logomark.png" alt="ReadySetSiivous" width={28} height={28} />
                  <Text fw={700} size="lg" visibleFrom="xs">
                    ReadySet<Text component="span" c="indigo" fw={700}>Siivous</Text>
                  </Text>
                </Group>
              </Link>
            </Group>

            <Group gap="xs" wrap="nowrap">
              <Button component={Link} href={`/account/?tenant=${encodeURIComponent(authTenant())}`} variant="subtle" size="xs">My account</Button>
              {hasOwnWorkIdentity && <Button component={Link} href="/me/shifts/" variant="light" size="xs">My shifts</Button>}
              <PwaInstallButton variant="light" size="xs" compact />
              <Tooltip label="Sign out" withArrow>
                <Button
                  variant="subtle"
                  color="gray"
                  size="xs"
                  leftSection={<LogOut size={16} />}
                  onClick={() => logout()}
                  visibleFrom="xs"
                >
                  Sign out
                </Button>
              </Tooltip>
              <ActionIcon
                variant="subtle"
                color="gray"
                size="md"
                onClick={() => logout()}
                hiddenFrom="xs"
                title="Sign out"
              >
                <LogOut size={18} />
              </ActionIcon>
            </Group>
          </Group>
        </AppShell.Header>

        <AppShell.Navbar p="md">
          <Group mb="lg" gap="xs">
            <Image src="/brand/readyset-logomark.png" alt="ReadySetSiivous" width={28} height={28} />
            <Text fw={700}>ReadySet<Text component="span" c="indigo">Siivous</Text></Text>
          </Group>
          <Stack gap={4}>
            {NAV.map((item) => {
              const href = item.href ? `${base}${item.href}/` : base;
              const active = item.href === ''
                ? pathname === '/admin' || pathname === '/admin/'
                : pathname.startsWith(base + item.href);
              return (
                <NavLink
                  key={item.href || 'dashboard'}
                  component={Link}
                  href={href}
                  label={item.label}
                  leftSection={<item.icon size={18} />}
                  active={active}
                  onClick={close}
                />
              );
            })}
          </Stack>
          <AppShell.Section mt="auto" pt="md">
            <Stack gap="xs">
              <PwaInstallButton variant="outline" size="sm" />
              <Button
                variant="subtle"
                color="gray"
                fullWidth
                justify="start"
                leftSection={<LogOut size={16} />}
                onClick={() => logout()}
              >
                Sign out
              </Button>
            </Stack>
          </AppShell.Section>
        </AppShell.Navbar>

        <AppShell.Main>{children}</AppShell.Main>
      </AppShell>
    </MantineProvider>
  );
}
