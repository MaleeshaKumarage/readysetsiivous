'use client';

import Link from 'next/link';
import { Sparkles, FileSignature, Users, Building2, CalendarClock, Receipt, CheckSquare, ArrowUpRight } from 'lucide-react';
import { Title, SimpleGrid, Paper, Group, Text, ThemeIcon, Stack } from '@mantine/core';
import { DashboardStatus } from '@/components/admin/DashboardStatus';
import { DashboardCharts } from '@/components/admin/DashboardCharts';
import { portalTheme } from '@/lib/portalTheme';
import Image from 'next/image';
import './office.css';

const SECTIONS = [
  { title: 'Operations', description: 'Plan the work and keep service quality on track.', items: [
    { href: 'shifts', title: 'Shifts', description: 'Create and assign recurring shifts across company branches.', icon: CalendarClock },
    { href: 'quality-cycle', title: 'Quality Cycle', description: 'Manage quality cycles, checklists and service follow-up.', icon: CheckSquare },
  ] },
  { title: 'Customers', description: 'Move from the first conversation to an active customer.', items: [
    { href: 'leads', title: 'Leads', description: 'Manage prospects, contact details and customer conversion.', icon: Users },
    { href: 'companies', title: 'Companies', description: 'Manage customer companies and branch locations.', icon: Building2 },
    { href: 'agreements', title: 'Agreements', description: 'Upload agreements and collect e-signatures.', icon: FileSignature },
  ] },
  { title: 'Business administration', description: 'Keep your people, billing and service catalog up to date.', items: [
    { href: 'employees', title: 'Employees', description: 'Manage staff profiles, availability, skills and certifications.', icon: Users },
    { href: 'invoices', title: 'Invoices', description: 'Preview billing, issue invoices and manage payment status.', icon: Receipt },
    { href: 'services', title: 'Services', description: 'Manage cleaning services, prices and descriptions.', icon: Sparkles },
  ] },
];

export default function AdminDashboardPage() {
  return <Stack gap="xl" style={portalTheme}>
    <div className="office-hero" style={portalTheme}>
      <Image src="/brand/readyset-primary-logo.png" alt="ReadySetSiivous" width={56} height={56} />
      <div><Title order={1}>Office portal</Title><Text c="dimmed" mt={4}>Your workspace for customers, people and daily operations.</Text></div>
    </div>
    <DashboardStatus />
    <DashboardCharts />
    {SECTIONS.map(section => <section key={section.title} aria-labelledby={`section-${section.title.replaceAll(' ', '-').toLowerCase()}`}>
      <Title order={2} size="h3" id={`section-${section.title.replaceAll(' ', '-').toLowerCase()}`}>{section.title}</Title>
      <Text c="dimmed" size="sm" mt={4} mb="md">{section.description}</Text>
      <SimpleGrid cols={{ base: 1, sm: 2, lg: Math.min(3, section.items.length) }} spacing="md">
        {section.items.map(item => <Paper key={item.href} component={Link} href={`/admin/${item.href}/`} p={{ base: 'md', sm: 'lg' }} withBorder className="admin-destination office-card">
          <Group justify="space-between" mb="md"><ThemeIcon size={42} variant="light" radius="md"><item.icon size={21} aria-hidden="true" /></ThemeIcon><ArrowUpRight size={18} aria-hidden="true" className="admin-destination-arrow" /></Group>
          <Text fw={650} mb={6}>{item.title}</Text><Text size="sm" c="dimmed">{item.description}</Text>
        </Paper>)}
      </SimpleGrid>
    </section>)}
  </Stack>;
}
