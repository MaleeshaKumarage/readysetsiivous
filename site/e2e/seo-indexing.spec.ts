import { test, expect, type APIRequestContext } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const SITE = 'https://readysetsiivous.fi';
const LANGS = ['fi', 'en', 'sv'] as const;

// Server-rendered head of a route, as a crawler receives it.
async function head(request: APIRequestContext, path: string): Promise<string> {
  const response = await request.get(path, { timeout: 60000 });
  expect(response.status(), `${path} status`).toBe(200);
  return response.text();
}

function robotsOf(html: string): string | null {
  return html.match(/<meta name="robots" content="([^"]*)"/)?.[1] ?? null;
}

function canonicalOf(html: string): string | null {
  return html.match(/<link rel="canonical" href="([^"]*)"/)?.[1] ?? null;
}

test.describe('SEO: canonical and language alternates', () => {
  for (const lang of LANGS) {
    for (const path of ['varaus/', 'privacy/', 'terms/', 'card/']) {
      test(`${lang}/${path} declares itself as canonical`, async ({ request }) => {
        const html = await head(request, `/${lang}/${path}`);
        expect(canonicalOf(html)).toBe(`${SITE}/${lang}/${path}`);
        for (const other of LANGS) {
          // React renders the attribute as hrefLang, so match it case-insensitively.
          expect(html).toMatch(new RegExp(`hreflang="${other}" href="${SITE}/${other}/${path}"`, 'i'));
        }
        expect(robotsOf(html)).not.toContain('noindex');
      });
    }

    test(`${lang}/ homepage canonical is the language homepage`, async ({ request }) => {
      const html = await head(request, `/${lang}/`);
      expect(canonicalOf(html)).toBe(`${SITE}/${lang}/`);
    });
  }
});

test.describe('SEO: unique page titles', () => {
  const titles: Record<string, Record<string, string>> = {
    fi: { 'varaus/': 'Pyydä siivousta', 'privacy/': 'Tietosuojaseloste', 'terms/': 'Käyttöehdot' },
    en: { 'varaus/': 'Request a cleaning', 'privacy/': 'Privacy Policy', 'terms/': 'Terms of Service' },
    sv: { 'varaus/': 'Begär städning', 'privacy/': 'Integritetspolicy', 'terms/': 'Användarvillkor' },
  };
  for (const lang of LANGS) {
    for (const [path, title] of Object.entries(titles[lang])) {
      test(`${lang}/${path} has its own title`, async ({ request }) => {
        const html = await head(request, `/${lang}/${path}`);
        expect(html).toContain(`<title>${title} | ReadySetSiivous</title>`);
      });
    }
  }
});

test.describe('SEO: html lang on prerendered pages', () => {
  // Reads the built export, so it runs after `npm run build` (postbuild rewrites the lang attribute).
  for (const lang of ['en', 'sv'] as const) {
    test(`out/${lang}/ declares lang="${lang}"`, async () => {
      const file = path.join(__dirname, '..', 'out', lang, 'index.html');
      test.skip(!fs.existsSync(file), 'Run `npm run build` first: the export is missing.');
      const html = fs.readFileSync(file, 'utf8');
      expect(html).toMatch(new RegExp(`<html lang="${lang}"`));
    });
  }
});

test.describe('SEO: noindex on non-public routes', () => {
  const noindexRoutes = [
    '/fi/booking/',
    '/fi/sign/',
    '/account/',
    '/quality-cycle/',
    '/quality-report/',
    '/admin/',
    '/manager/',
    '/partner/',
    '/me/shifts/',
    '/me/inventory/',
  ];
  for (const path of noindexRoutes) {
    test(`${path} is excluded from the index`, async ({ request }) => {
      const html = await head(request, path);
      expect(robotsOf(html)).toContain('noindex');
    });
  }
});

test.describe('SEO: sitemap', () => {
  test('lists every public page in every language and no token or staff pages', async ({ request }) => {
    const response = await request.get('/sitemap.xml');
    expect(response.status()).toBe(200);
    const xml = await response.text();
    const locs = [...xml.matchAll(/<loc>([^<]*)<\/loc>/g)].map((m) => m[1]);
    for (const lang of LANGS) {
      for (const path of ['/', '/card/', '/varaus/', '/privacy/', '/terms/']) {
        expect(locs).toContain(`${SITE}/${lang}${path}`);
      }
    }
    expect(locs.some((loc) => /\/(booking|sign|admin|manager|partner|account|me)\//.test(loc))).toBe(false);
  });
});
