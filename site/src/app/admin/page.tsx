'use client';

import Link from 'next/link';
import { Sparkles, FileSignature, Users, Building2, CalendarClock } from 'lucide-react';
import { Title, SimpleGrid, Paper, Group, Text, ThemeIcon } from '@mantine/core';

const SECTIONS = [
  { href: 'services', title: 'Services', description: 'Manage cleaning services, prices and descriptions.', icon: Sparkles },
  { href: 'agreements', title: 'Agreements', description: 'Upload agreements and collect e-signatures.', icon: FileSignature },
  { href: 'employees', title: 'Employees', description: 'Manage staff profiles, availability, skills and certifications.', icon: Users },
  { href: 'companies', title: 'Companies', description: 'Manage customer companies and branch locations.', icon: Building2 },
  { href: 'shifts', title: 'Shifts', description: 'Create and assign recurring shifts across company branches.', icon: CalendarClock },
] as const;

export default function AdminDashboardPage() {
  return (
    <>
      <Title order={2} mb="md">Dashboard</Title>
      <SimpleGrid cols={{ base: 1, sm: 2, xl: 3 }}>
        {SECTIONS.map((s) => (
          <Paper key={s.href} component={Link} href={`/admin/${s.href}/`} p="lg" withBorder radius="md">
            <Group mb="xs">
              <ThemeIcon size="lg" variant="light"><s.icon size={18} /></ThemeIcon>
              <Text fw={600} c="white">{s.title}</Text>
            </Group>
            <Text size="sm" c="dimmed">{s.description}</Text>
          </Paper>
        ))}
      </SimpleGrid>
    </>
  );
}
