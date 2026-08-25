import type { Metadata } from 'next';
import { LanguageProvider } from '@/context/LanguageContext';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';
import { getDictionary } from '@/i18n';
import SchemaScript from '@/components/SchemaScript';

export function generateStaticParams() {
  return [{ lang: 'fi' }, { lang: 'en' }, { lang: 'sv' }];
}

export async function generateMetadata({
  params,
}: {
  params: { lang: string };
}): Promise<Metadata> {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  const dict = getDictionary(lang);
  const baseUrl = 'https://readysetsiivous.fi';

  const alternates: Record<string, string> = {
    fi: `${baseUrl}/fi/`,
    en: `${baseUrl}/en/`,
    sv: `${baseUrl}/sv/`,
    'x-default': `${baseUrl}/fi/`,
  };

  return {
    title: dict.site.title,
    description: dict.site.description,
    metadataBase: new URL(baseUrl),
    alternates: {
      canonical: `${baseUrl}/${lang}/`,
      languages: alternates,
    },
    openGraph: {
      title: dict.site.title,
      description: dict.site.description,
      type: 'website',
      locale: dict.site.locale,
      siteName: 'ReadySetSiivous',
      url: `${baseUrl}/${lang}/`,
    },
    keywords: [
      'cleaning',
      'siivous',
      'städning',
      'Helsinki',
      'Espoo',
      'Vantaa',
      'home cleaning',
      'office cleaning',
      'move-out cleaning',
      'deep cleaning',
      'kotisiivous',
      'syväsiivous',
      'muutosiivous',
      'toimistosiivous',
      'hemstädning',
      'storstädning',
      'flyttstädning',
      'kontorsstädning',
    ],
  };
}

export default function LangLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: { lang: string };
}) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  const dict = getDictionary(lang);

  return (
    <>
      <SchemaScript lang={lang} dict={dict} />
      <LanguageProvider initialLang={lang}>{children}</LanguageProvider>
    </>
  );
}
