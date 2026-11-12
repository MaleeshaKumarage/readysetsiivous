import type { Metadata } from 'next';
import { NOINDEX } from '@/lib/seo';

// Signed-in or token-shared workspace: keep out of search results.
export const metadata: Metadata = { robots: NOINDEX };

export default function AccountLayout({ children }: { children: React.ReactNode }) {
  return children;
}
