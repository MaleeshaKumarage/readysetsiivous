import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import ManagerLayoutClient from './ManagerLayoutClient';
import { NOINDEX } from '@/lib/seo';

// Staff workspace: never indexed. The interactive shell lives in ManagerLayoutClient.
export const metadata: Metadata = { robots: NOINDEX };

export default function ManagerLayout({ children }: { children: ReactNode }) {
  return <ManagerLayoutClient>{children}</ManagerLayoutClient>;
}
