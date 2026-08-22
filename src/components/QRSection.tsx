'use client';

import { useLanguage } from '@/hooks/useLanguage';
import { baseUrl } from '@/lib/whatsapp';
import type { Language } from '@/i18n';
import { Reveal } from './motion';

export default function QRSection() {
  const { t, lang } = useLanguage();

  const cardUrl = `https://maleeshakumarage.github.io/readysetsiivous/${lang}/card/`;
  const qrSrc = `https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(cardUrl)}&bgcolor=ffffff&color=166534&format=png`;

  const labels: Record<Language, { title: string; desc: string; step1: string; step2: string }> = {
    fi: {
      title: 'Skannaa & tallenna yhteystiedot',
      desc: 'Avaa kamera ja skannaa QR-koodi tallentaaksesi yhteystietomme suoraan puhelimeesi.',
      step1: 'Avaa puhelimen kamera',
      step2: 'Skannaa koodi → tallenna tai soita',
    },
    en: {
      title: 'Scan & Save Our Contact',
      desc: 'Open your phone camera and scan this QR code to instantly save our contact details or call us.',
      step1: 'Open your phone camera',
      step2: 'Scan code → save or call directly',
    },
    sv: {
      title: 'Skanna & spara vår kontakt',
      desc: 'Öppna telefonens kamera och skanna QR-koden för att spara våra kontaktuppgifter direkt.',
      step1: 'Öppna telefonens kamera',
      step2: 'Skanna koden → spara eller ring',
    },
  };

  const L = labels[lang];

  return (
    <section className="bg-gray-50 dark:bg-gray-900 section-padding">
      <div className="container-page">
        <Reveal className="max-w-sm mx-auto text-center">
          <h2 className="text-xl font-extrabold text-gray-900 dark:text-gray-50">
            📱 {L.title}
          </h2>
          <p className="mt-2 text-sm text-gray-500 dark:text-gray-400">
            {L.desc}
          </p>

          {/* QR Code */}
          <div className="mt-6 inline-block bg-white dark:bg-gray-800 p-4 rounded-2xl shadow-lg border border-gray-100 dark:border-gray-700">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={qrSrc}
              alt={`QR Code for ReadySetSiivous contact card — ${cardUrl}`}
              width={200}
              height={200}
              className="w-48 h-48 sm:w-52 sm:h-52 mx-auto"
              loading="lazy"
            />
          </div>

          {/* Steps */}
          <div className="mt-5 flex items-center justify-center gap-6 text-xs text-gray-500 dark:text-gray-400">
            <span className="flex items-center gap-1.5">
              <span className="w-5 h-5 rounded-full bg-brand-100 dark:bg-brand-500/20 text-brand-700 dark:text-brand-400 font-bold text-[10px] flex items-center justify-center">1</span>
              {L.step1}
            </span>
            <span className="flex items-center gap-1.5">
              <span className="w-5 h-5 rounded-full bg-brand-100 dark:bg-brand-500/20 text-brand-700 dark:text-brand-400 font-bold text-[10px] flex items-center justify-center">2</span>
              {L.step2}
            </span>
          </div>

          {/* Direct link fallback */}
          <a
            href={cardUrl}
            className="mt-4 inline-block text-xs text-brand-600 dark:text-brand-400 hover:underline"
          >
            {cardUrl}
          </a>
        </Reveal>
      </div>
    </section>
  );
}
