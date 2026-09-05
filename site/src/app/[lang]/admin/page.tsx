'use client';

import { useEffect, useState } from 'react';
import Navbar from '@/components/Navbar';
import { initAuth, isAuthenticated, login, logout } from '@/lib/auth';
import { type Language, DEFAULT_LANGUAGE } from '@/i18n';

export default function AdminPage({ params }: { params: { lang: string } }) {
  const lang = (params.lang as Language) ?? DEFAULT_LANGUAGE;
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);

  useEffect(() => {
    initAuth().then(() => {
      setAuthed(isAuthenticated());
      setReady(true);
    });
  }, []);

  return (
    <>
      <Navbar lang={lang} />
      <main>
        <section className="bg-gray-50 dark:bg-gray-900 section-padding">
          <div className="container-page">
            <div className="mx-auto max-w-md rounded-2xl bg-white dark:bg-gray-800 border border-gray-100 dark:border-gray-700 shadow-sm p-8 text-center">
              {!ready ? (
                <p className="text-gray-500 dark:text-gray-400">Checking login…</p>
              ) : authed ? (
                <>
                  <h1 className="mb-2 text-2xl font-bold text-gray-900 dark:text-gray-50">
                    Admin
                  </h1>
                  <p className="mb-6 text-sm text-gray-500 dark:text-gray-400">
                    You are signed in.
                  </p>
                  <button
                    onClick={() => logout()}
                    className="rounded-full border border-gray-200 dark:border-gray-700 px-6 py-3 font-semibold text-gray-700 dark:text-gray-200 hover:border-red-300 hover:text-red-500 transition-all"
                  >
                    Sign out
                  </button>
                </>
              ) : (
                <>
                  <h1 className="mb-2 text-2xl font-bold text-gray-900 dark:text-gray-50">
                    Admin login
                  </h1>
                  <p className="mb-6 text-sm text-gray-500 dark:text-gray-400">
                    Admin access requires the admin role in the ReadySetSiivous Keycloak realm.
                  </p>
                  <button
                    onClick={() => login()}
                    className="inline-flex items-center gap-2 rounded-full bg-brand px-6 py-3 font-semibold text-accent-950 hover:bg-brand-400 transition-all"
                  >
                    Sign in with Keycloak
                  </button>
                </>
              )}
            </div>
          </div>
        </section>
      </main>
    </>
  );
}
