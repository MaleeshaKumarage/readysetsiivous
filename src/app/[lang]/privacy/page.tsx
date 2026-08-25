import { type Language, DEFAULT_LANGUAGE, getDictionary } from '@/i18n';
import Navbar from '@/components/Navbar';
import Footer from '@/components/Footer';
import FloatingWhatsApp from '@/components/FloatingWhatsApp';

export function generateStaticParams() {
  return [{ lang: 'fi' }, { lang: 'en' }, { lang: 'sv' }];
}

export default function PrivacyPage({
  params,
}: {
  params: { lang: string };
}) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  const dict = getDictionary(lang);

  return (
    <>
      <Navbar lang={lang} />
      <main className="bg-white dark:bg-gray-950">
        <div className="container-page max-w-3xl section-padding">
          <h1 className="heading-xl text-gray-900 dark:text-gray-50">{dict.privacy.title}</h1>
          <p className="mt-3 text-sm text-gray-500 dark:text-gray-400">{dict.privacy.updated}</p>
          <div className="mt-10 space-y-8">
            {dict.privacy.sections.map((s) => (
              <section key={s.h}>
                <h2 className="text-lg font-bold text-gray-900 dark:text-gray-100">{s.h}</h2>
                <p className="mt-2 text-gray-600 dark:text-gray-300 leading-relaxed">{s.p}</p>
              </section>
            ))}
          </div>
        </div>
      </main>
      <Footer />
      <FloatingWhatsApp />
    </>
  );
}
