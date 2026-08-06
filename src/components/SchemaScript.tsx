import { type Language } from '@/i18n';

interface SchemaProps {
  lang: Language;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  dict: any;
}

export default function SchemaScript({ lang, dict }: SchemaProps) {
  const schema = {
    '@context': 'https://schema.org',
    '@type': 'CleaningService',
    name: dict.schema.name,
    description: dict.schema.description,
    url: `https://maleeshakumarage.github.io/readysetsiivous/${lang}/`,
    areaServed: [
      {
        '@type': 'City',
        name: 'Helsinki',
        sameAs: 'https://en.wikipedia.org/wiki/Helsinki',
      },
      {
        '@type': 'City',
        name: 'Espoo',
        sameAs: 'https://en.wikipedia.org/wiki/Espoo',
      },
      {
        '@type': 'City',
        name: 'Vantaa',
        sameAs: 'https://en.wikipedia.org/wiki/Vantaa',
      },
      {
        '@type': 'City',
        name: 'Kauniainen',
        sameAs: 'https://en.wikipedia.org/wiki/Kauniainen',
      },
    ],
    priceRange: dict.schema.priceRange,
    openingHoursSpecification: [
      {
        '@type': 'OpeningHoursSpecification',
        dayOfWeek: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
        opens: '07:00',
        closes: '21:00',
      },
      {
        '@type': 'OpeningHoursSpecification',
        dayOfWeek: 'Saturday',
        opens: '09:00',
        closes: '18:00',
      },
    ],
    contactPoint: {
      '@type': 'ContactPoint',
      contactType: 'customer service',
      availableLanguage: ['Finnish', 'English', 'Swedish'],
      url: 'https://wa.me/358XXXXXXXXX',
    },
    sameAs: ['https://wa.me/358XXXXXXXXX'],
  };

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: JSON.stringify(schema) }}
    />
  );
}
