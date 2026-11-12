import type { Metadata } from 'next';
import { localizedPageMetadata } from '@/lib/seo';
import { getDictionary, type Language, DEFAULT_LANGUAGE } from '@/i18n';

export function generateMetadata({ params }: { params: { lang: string } }): Metadata {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  return localizedPageMetadata(lang, '/terms/', getDictionary(lang).terms.title);
}

export default function TermsLayout({ children }: { children: React.ReactNode }) {
  return children;
}
