import { type Language, DEFAULT_LANGUAGE, getDictionary } from '@/i18n';
import type { Metadata } from 'next';
import CardPageClient from './CardPageClient';

export function generateStaticParams() {
  return [
    { lang: 'fi' },
    { lang: 'en' },
    { lang: 'sv' },
  ];
}

export function generateMetadata({
  params,
}: {
  params: { lang: string };
}): Metadata {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  const dict = getDictionary(lang);

  return {
    title: `ReadySetSiivous — ${lang === 'fi' ? 'Digitaalinen käyntikortti' : lang === 'sv' ? 'Digitalt visitkort' : 'Digital Business Card'}`,
    description: dict.site.description,
  };
}

export default function CardPage({
  params,
}: {
  params: { lang: string };
}) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  return <CardPageClient lang={lang} />;
}
