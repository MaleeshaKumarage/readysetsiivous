'use client';

import '@mantine/core/styles.css';
import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  MantineProvider, AppShell, NavLink, Group, Text, Button, Stack, Paper, createTheme, Burger, ActionIcon, Tooltip,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  LayoutDashboard, Sparkles, FileSignature, Users, Building2, CalendarClock, CheckSquare, LogOut, LogIn,
} from 'lucide-react';
import { initAuth, isAuthenticated, login, logout } from '@/lib/auth';
import { PwaInstallButton } from '@/components/PwaInstallButton';

const NAV = [
  { href: '', label: 'Dashboard', icon: LayoutDashboard },
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
  const [opened, { toggle, close }] = useDisclosure(false);
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
    root.setAttribute('data-mantine-color-scheme', 'dark');
    return () => {
      root.classList.remove('dark');
      root.removeAttribute('data-mantine-color-scheme');
    };
  }, []);

  const base = `/admin/`;

  if (!ready) {
    return (
      <MantineProvider theme={adminTheme} forceColorScheme="dark">
        <Group justify="center" h="100vh">
          <Text c="dimmed">Checking login…</Text>
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
                  <LayoutDashboard size={22} className="text-indigo-500" />
                  <Text fw={700} size="lg" visibleFrom="xs">
                    ReadySet<Text component="span" c="indigo" fw={700}>Siivous</Text>
                  </Text>
                </Group>
              </Link>
            </Group>

            <Group gap="xs" wrap="nowrap">
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
            <LayoutDashboard size={20} />
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
