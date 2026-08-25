'use client';

import { getWhatsAppChatUrl, buildWhatsAppMessage, baseUrl } from '@/lib/whatsapp';
import { useLanguage } from '@/hooks/useLanguage';
import { LanguageProvider } from '@/context/LanguageContext';
import type { Language } from '@/i18n';

const CARD_LABELS: Record<Language, {
  tagline: string;
  saveContact: string;
  chatWhatsApp: string;
  callUs: string;
  sendEmail: string;
  visitSite: string;
  services: string;
  taxNote: string;
}> = {
  fi: {
    tagline: 'Ammattitaitoiset siivouspalvelut',
    saveContact: '📇 Tallenna yhteystiedot',
    chatWhatsApp: '💬 Keskustele WhatsAppissa',
    callUs: '📞 Soita meille',
    sendEmail: '✉️ Lähetä sähköpostia',
    visitSite: '🌐 Siirry kotisivuille',
    services: 'Kotisiivous • Syväsiivous • Muutosiivous • Toimistosiivous',
    taxNote: 'Muista kotitalousvähennys – säästä jopa 60%!',
  },
  en: {
    tagline: 'Professional Cleaning Services',
    saveContact: '📇 Save to Contacts',
    chatWhatsApp: '💬 Chat on WhatsApp',
    callUs: '📞 Call Us',
    sendEmail: '✉️ Send an Email',
    visitSite: '🌐 Visit Full Website',
    services: 'Home Cleaning • Deep Cleaning • Move-out • Office',
    taxNote: 'Finnish tax deduction – save up to 60%!',
  },
  sv: {
    tagline: 'Professionella städtjänster',
    saveContact: '📇 Spara kontakt',
    chatWhatsApp: '💬 Chatta på WhatsApp',
    callUs: '📞 Ring oss',
    sendEmail: '✉️ Skicka e-post',
    visitSite: '🌐 Besök webbplatsen',
    services: 'Hemstädning • Storstädning • Flyttstädning • Kontor',
    taxNote: 'Hushållsavdrag – spara upp till 60%!',
  },
};

function CardContent({ lang }: { lang: Language }) {
  const { t } = useLanguage();
  const L = CARD_LABELS[lang];

  const waMessage = buildWhatsAppMessage(
    { service: '', size: '', city: '', date: '' },
    {
      greeting: t('whatsapp.greeting'),
      serviceLabel: t('whatsapp.serviceLabel'),
      sizeLabel: t('whatsapp.sizeLabel'),
      cityLabel: t('whatsapp.cityLabel'),
      dateLabel: t('whatsapp.dateLabel'),
      languageNote: t('whatsapp.languageNote'),
    }
  ).split('\n')[0]; // Just the greeting line

  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 via-white to-accent-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        {/* Card */}
        <div className="bg-white dark:bg-gray-800 rounded-3xl shadow-2xl dark:shadow-gray-950/50 overflow-hidden">
          {/* Header */}
          <div className="bg-gradient-to-r from-brand-500 to-brand-600 px-6 py-8 text-center">
            <div className="w-16 h-16 rounded-2xl bg-white/20 flex items-center justify-center mx-auto mb-3">
              <svg className="w-8 h-8 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z" />
              </svg>
            </div>
            <h1 className="text-xl font-extrabold text-white">
              ReadySet<span className="text-brand-200">Siivous</span>
            </h1>
            <p className="mt-1 text-brand-100 text-sm">{L.tagline}</p>
          </div>

          {/* Actions */}
          <div className="p-5 space-y-3">
            {/* Save Contact */}
            <a
              href={baseUrl('/contact.vcf')}
              download
              className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-brand-500 text-white text-sm font-bold rounded-xl hover:bg-brand-600 transition-colors shadow-md shadow-brand-200 dark:shadow-brand-900/30"
            >
              {L.saveContact}
            </a>

            {/* Quick actions */}
            <a
              href={getWhatsAppChatUrl()}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-whatsapp/10 text-whatsapp dark:text-green-400 text-sm font-semibold rounded-xl hover:bg-whatsapp/20 transition-colors border border-whatsapp/20"
            >
              {L.chatWhatsApp}
            </a>

            <a
              href="tel:+358468044231"
              className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-gray-50 dark:bg-gray-700 text-gray-700 dark:text-gray-200 text-sm font-semibold rounded-xl hover:bg-gray-100 dark:hover:bg-gray-600 transition-colors"
            >
              {L.callUs}
            </a>

            <a
              href="mailto:info@readysetsiivous.fi"
              className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-gray-50 dark:bg-gray-700 text-gray-700 dark:text-gray-200 text-sm font-semibold rounded-xl hover:bg-gray-100 dark:hover:bg-gray-600 transition-colors"
            >
              {L.sendEmail}
            </a>

            <a
              href={baseUrl(`/${lang}/`)}
              className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-gray-50 dark:bg-gray-700 text-gray-700 dark:text-gray-200 text-sm font-semibold rounded-xl hover:bg-gray-100 dark:hover:bg-gray-600 transition-colors"
            >
              {L.visitSite}
            </a>
          </div>

          {/* Info footer */}
          <div className="px-5 pb-5 text-center space-y-2">
            <p className="text-xs text-gray-500 dark:text-gray-400">{L.services}</p>
            <div className="inline-block px-3 py-1.5 bg-brand-50 dark:bg-brand-500/10 rounded-full">
              <p className="text-xs font-semibold text-brand-700 dark:text-brand-400">{L.taxNote}</p>
            </div>
          </div>
        </div>

        {/* Footer note */}
        <p className="mt-4 text-center text-xs text-gray-400 dark:text-gray-500">
          {lang === 'fi'
            ? 'Skannaa QR-koodi tallentaaksesi yhteystiedot'
            : lang === 'sv'
            ? 'Skanna QR-koden för att spara kontakten'
            : 'Scan the QR code to save our contact'}
        </p>
      </div>
    </div>
  );
}

export default function CardPageClient({ lang }: { lang: Language }) {
  return (
    <LanguageProvider initialLang={lang}>
      <CardContent lang={lang} />
    </LanguageProvider>
  );
}
