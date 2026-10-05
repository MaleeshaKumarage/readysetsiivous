'use client';

import '@mantine/core/styles.css';
import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  MantineProvider, AppShell, NavLink, Group, Text, Button, Stack, Paper, createTheme,
} from '@mantine/core';
import {
  LayoutDashboard, Sparkles, FileSignature, Users, Building2, CalendarClock, CheckSquare, LogOut, LogIn,
} from 'lucide-react';
import { initAuth, isAuthenticated, login, logout } from '@/lib/auth';

const NAV = [
  { href: 'services', label: 'Services', icon: Sparkles },
  { href: 'agreements', label: 'Agreements', icon: FileSignature },
  { href: 'employees', label: 'Employees', icon: Users },
  { href: 'companies', label: 'Companies', icon: Building2 },
  { href: 'shifts', label: 'Shifts', icon: CalendarClock },
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
  const pathname = usePathname() ?? '';

  useEffect(() => {
    initAuth().then(() => {
      setAuthed(isAuthenticated());
      setReady(true);
    });
  }, []);

  // Keep the site's dark class for any pages still using shadcn components.
  useEffect(() => {
    const root = document.documentElement;
    root.classList.add('dark');
    return () => root.classList.remove('dark');
  }, []);

  const base = `/admin/`;

  if (!ready) {
    return <MantineProvider theme={adminTheme} defaultColorScheme="dark"><Text>Checking login…</Text></MantineProvider>;
  }

  if (!authed) {
    return (
      <MantineProvider theme={adminTheme} defaultColorScheme="dark">
        <Group justify="center" h="100vh">
          <Paper p="xl" radius="md" w={380} withBorder>
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
    <MantineProvider theme={adminTheme} defaultColorScheme="dark">
      <AppShell
        navbar={{ width: 260, breakpoint: 'sm' }}
        padding="md"
      >
        <AppShell.Navbar p="md">
          <Group mb="lg" gap="xs">
            <LayoutDashboard size={20} />
            <Text fw={700}>ReadySet<span>Siivous</span></Text>
          </Group>
          <Stack gap={4}>
            {NAV.map((item) => {
              const active = pathname.startsWith(base + item.href);
              return (
                <NavLink
                  key={item.href}
                  component={Link}
                  href={base + item.href + '/'}
                  label={item.label}
                  leftSection={<item.icon size={18} />}
                  active={active}
                />
              );
            })}
          </Stack>
          <AppShell.Section mt="auto">
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
          </AppShell.Section>
        </AppShell.Navbar>

        <AppShell.Main>{children}</AppShell.Main>
      </AppShell>
    </MantineProvider>
  );
}
