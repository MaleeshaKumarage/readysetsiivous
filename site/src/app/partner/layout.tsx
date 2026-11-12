import type { Metadata, Viewport } from 'next';
import { NOINDEX } from '@/lib/seo';

export const viewport: Viewport = { viewportFit: 'cover' };

// Partner portal: never indexed.
export const metadata: Metadata = { robots: NOINDEX };

export default function PartnerLayout({ children }: { children: React.ReactNode }) {
  return children;
}
