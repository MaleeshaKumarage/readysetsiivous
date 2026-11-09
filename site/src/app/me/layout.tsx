import type { Metadata, Viewport } from 'next';
export const viewport: Viewport = { viewportFit: 'cover' };
export const metadata: Metadata = {
  manifest: '/employee-manifest.json',
  appleWebApp: { capable: true, statusBarStyle: 'black-translucent', title: 'ReadySetSiivous My shifts' },
};
export default function EmployeeLayout({ children }: { children: React.ReactNode }) { return children; }
