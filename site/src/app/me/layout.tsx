import type { Metadata, Viewport } from 'next';
import { NOINDEX } from '@/lib/seo';
export const viewport: Viewport = { viewportFit: 'cover' };
export const metadata: Metadata = {
  manifest: '/employee-manifest.json',
  robots: NOINDEX,
  appleWebApp: { capable: true, statusBarStyle: 'black-translucent', title: 'ReadySetSiivous My shifts' },
};
export default function EmployeeLayout({ children }: { children: React.ReactNode }) { return children; }
