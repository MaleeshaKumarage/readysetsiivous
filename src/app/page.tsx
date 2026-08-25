import LanguageRedirect from '@/components/LanguageRedirect';

/**
 * Root landing page.
 * Users with JavaScript are redirected to their language (/fi, /en, /sv) by
 * <LanguageRedirect>; the links below remain as a server-rendered fallback
 * for crawlers and no-JS users.
 */
export default function RootPage() {
  return (
    <>
      <LanguageRedirect />
      <main className="min-h-screen flex flex-col items-center justify-center gap-8 bg-white dark:bg-accent-950 px-6">
        <p className="text-lg font-extrabold text-gray-900 dark:text-gray-50 tracking-tight">
          ReadySet<span className="text-brand-600 dark:text-brand-400">Siivous</span>
        </p>
        <div className="flex items-center gap-4">
          <a href="/fi/" className="px-6 py-3 rounded-full bg-brand-500 text-white text-sm font-semibold hover:bg-brand-600 transition-colors">
            Suomi
          </a>
          <a href="/en/" className="px-6 py-3 rounded-full border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-200 text-sm font-semibold hover:border-brand-400 transition-colors">
            English
          </a>
          <a href="/sv/" className="px-6 py-3 rounded-full border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-200 text-sm font-semibold hover:border-brand-400 transition-colors">
            Svenska
          </a>
        </div>
      </main>
    </>
  );
}
