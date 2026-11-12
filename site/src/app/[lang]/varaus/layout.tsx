import type { Metadata } from 'next';
import { localizedPageMetadata } from '@/lib/seo';
import { publicUx } from '@/i18n/publicUx';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';

export function generateMetadata({ params }: { params: { lang: string } }): Metadata {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  return localizedPageMetadata(lang, '/varaus/', publicUx(lang).book);
}

export default function VarausLayout({ children }: { children: React.ReactNode }) {
  return children;
}
