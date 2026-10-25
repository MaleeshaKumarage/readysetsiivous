'use client';

import '@mantine/core/styles.css';
import './admin.css';
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import Link from 'next/link';
import Image from 'next/image';
import { usePathname } from 'next/navigation';
import {
  MantineProvider, AppShell, NavLink, Group, Text, Button, Stack, Paper, createTheme, Burger, ActionIcon, Tooltip,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  LayoutDashboard, Sparkles, FileSignature, Users, Building2, CalendarClock, CheckSquare, Receipt, LogOut, LogIn, UserRound,
} from 'lucide-react';
import { initAuth, isAuthenticated, isAdmin, authTenant, refreshToken, login, logout } from '@/lib/auth';
import { API_URL } from '@/lib/api';
import { PwaInstallButton } from '@/components/PwaInstallButton';

const NAV_GROUPS = [
  { label: 'Workspace', items: [{ href: '', label: 'Dashboard', icon: LayoutDashboard }] },
  { label: 'Operations', items: [{ href: 'shifts', label: 'Shifts', icon: CalendarClock }, { href: 'quality-cycle', label: 'Quality Cycle', icon: CheckSquare }] },
  { label: 'Customers', items: [{ href: 'leads', label: 'Leads', icon: Users }, { href: 'companies', label: 'Companies', icon: Building2 }, { href: 'agreements', label: 'Agreements', icon: FileSignature }] },
  { label: 'People', items: [{ href: 'employees', label: 'Employees', icon: Users }] },
  { label: 'Finance', items: [{ href: 'invoices', label: 'Invoices', icon: Receipt }] },
  { label: 'Catalog', items: [{ href: 'services', label: 'Services', icon: Sparkles }] },
];

const adminTheme = createTheme({
  primaryColor: 'indigo',
  defaultRadius: 'md',
  fontFamily: 'Inter, system-ui, sans-serif',
  headings: { fontFamily: 'Inter, system-ui, sans-serif', fontWeight: '650' },
  components: {
    Button: Button.extend({ styles: { root: { minHeight: 44 } } }),
    ActionIcon: ActionIcon.extend({ styles: { root: { minWidth: 44, minHeight: 44 } } }),
    Paper: Paper.extend({ defaultProps: { radius: 'md' } }),
    NavLink: NavLink.extend({ styles: { root: { minHeight: 44, borderRadius: 8 } } }),
  },
});

export default function AdminLayout({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const [access, setAccess] = useState<'pending' | 'allowed' | 'denied' | 'error'>('pending');
  const [hasOwnWorkIdentity, setHasOwnWorkIdentity] = useState(false);
  const verification = useRef(0);
  // Capture before the child provider's layout effect writes its forced scheme.
  const previousScheme = useRef(typeof document === 'undefined' ? null : document.documentElement.getAttribute('data-mantine-color-scheme'));
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

  useLayoutEffect(() => {
    const root = document.documentElement;
    const previous = root.getAttribute('data-admin-theme');
    root.setAttribute('data-admin-theme', 'light');
    return () => {
      if (previous === null) root.removeAttribute('data-admin-theme');
      else root.setAttribute('data-admin-theme', previous);
      if (previousScheme.current === null) root.removeAttribute('data-mantine-color-scheme');
      else root.setAttribute('data-mantine-color-scheme', previousScheme.current);
    };
  }, []);

  const base = `/admin/`;

  if (!ready || (authed && access === 'pending')) {
    return (
      <MantineProvider theme={adminTheme} forceColorScheme="light">
        <Group justify="center" h="100vh">
          <Text c="dimmed">Checking login…</Text>
        </Group>
      </MantineProvider>
    );
  }

  if (authed && access !== 'allowed') {
    return (
      <MantineProvider theme={adminTheme} forceColorScheme="light">
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
      <MantineProvider theme={adminTheme} forceColorScheme="light">
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
    <MantineProvider theme={adminTheme} forceColorScheme="light">
      <AppShell
        className="admin-shell"
        header={{ height: 76 }}
        navbar={{ width: 256, breakpoint: 'sm', collapsed: { mobile: !opened } }}
        padding={{ base: 'md', sm: 'xl' }}
      >
        <AppShell.Header className="admin-header">
          <Group h="100%" justify="space-between" wrap="nowrap" gap="xs">
            <Group gap="xs" wrap="nowrap" className="admin-context">
              <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Toggle navigation" aria-expanded={opened} aria-controls="admin-navigation" />
              <div><Text size="xs" c="dimmed" className="admin-eyebrow">Tenant administration</Text><Text fw={600}>{NAV_GROUPS.flatMap(group => group.items).find(item => item.href ? pathname.startsWith(base + item.href) : pathname === '/admin' || pathname === '/admin/')?.label ?? 'Workspace'}</Text></div>
            </Group>
            <Group gap={4} wrap="nowrap">
              {hasOwnWorkIdentity && <Button component={Link} href="/me/shifts/" variant="light" size="xs">My shifts</Button>}
              <Button component={Link} href={`/account/?tenant=${encodeURIComponent(authTenant())}`} variant="subtle" size="xs" visibleFrom="xs">My account</Button>
              <ActionIcon component={Link} href={`/account/?tenant=${encodeURIComponent(authTenant())}`} variant="subtle" hiddenFrom="xs" aria-label="My account"><UserRound size={20} /></ActionIcon>
              <Tooltip label="Sign out" withArrow><ActionIcon variant="subtle" color="gray" onClick={() => logout()} aria-label="Sign out"><LogOut size={20} /></ActionIcon></Tooltip>
            </Group>
          </Group>
        </AppShell.Header>
        <AppShell.Navbar id="admin-navigation" className="admin-sidebar" p="md" aria-label="Administration">
          <Link href="/admin/" onClick={close} className="admin-brand">
            <Image src="/brand/readyset-logomark.png" alt="" width={32} height={32} />
            <Text fw={700}>ReadySet<Text component="span" c="indigo" fw={700}>Siivous</Text></Text>
          </Link>
          <AppShell.Section grow className="admin-nav-scroll">
            <Stack gap="md">
              {NAV_GROUPS.map(group => <div key={group.label}>
                <Text className="admin-nav-label" size="xs" fw={700} c="dimmed" mb={6}>{group.label}</Text>
                <Stack gap={4}>{group.items.map(item => {
                  const href = item.href ? `${base}${item.href}/` : base;
                  const active = item.href === '' ? pathname === '/admin' || pathname === '/admin/' : pathname.startsWith(base + item.href);
                  return <NavLink key={href} component={Link} href={href} label={item.label} leftSection={<item.icon size={18} aria-hidden="true" />} active={active} aria-current={active ? 'page' : undefined} onClick={close} />;
                })}</Stack>
              </div>)}
            </Stack>
          </AppShell.Section>
          <AppShell.Section className="admin-sidebar-footer" pt="md"><PwaInstallButton variant="light" size="sm" /></AppShell.Section>
        </AppShell.Navbar>
        <AppShell.Main><div className="admin-content">{children}</div></AppShell.Main>
      </AppShell>
    </MantineProvider>
  );
}
