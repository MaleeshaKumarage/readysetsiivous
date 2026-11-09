import type { Viewport } from 'next';
import AdminWorkspace from './AdminWorkspace';

// Only authenticated workspaces extend into display cutouts; the public website keeps its viewport.
export const viewport: Viewport = { viewportFit: 'cover' };

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminWorkspace>{children}</AdminWorkspace>;
}
