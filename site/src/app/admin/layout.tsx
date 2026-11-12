import type { Metadata, Viewport } from 'next';
import { NOINDEX } from '@/lib/seo';
import AdminWorkspace from './AdminWorkspace';

// Only authenticated workspaces extend into display cutouts; the public website keeps its viewport.
export const viewport: Viewport = { viewportFit: 'cover' };

// Staff workspace: never indexed.
export const metadata: Metadata = { robots: NOINDEX };

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminWorkspace>{children}</AdminWorkspace>;
}
