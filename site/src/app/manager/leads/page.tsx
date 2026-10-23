'use client';
import '@mantine/core/styles.css';
import { MantineProvider } from '@mantine/core';
import { ManagerLeadsWorkspace } from '@/components/leads/ManagerLeadsWorkspace';
export default function ManagerLeadsPage() { return <MantineProvider forceColorScheme="light"><ManagerLeadsWorkspace /></MantineProvider>; }
