import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Lighthouse's accessibility score is built on axe-core rules; this runs the same WCAG A/AA rule set
// on every public page in each language and fails on any violation.
const PAGES = ['', 'varaus/', 'card/', 'privacy/', 'terms/'];
const LANGS = ['fi', 'en', 'sv'] as const;

for (const lang of LANGS) {
  for (const path of PAGES) {
    test(`${lang}/${path || ''} has no WCAG 2.2 A/AA violations`, async ({ page }) => {
      await page.goto(`/${lang}/${path}`);
      await page.waitForLoadState('networkidle');
      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        .analyze();
      const summary = results.violations.map(v => `${v.id} (${v.impact}): ${v.nodes.length} node(s) — ${v.help}`);
      expect(summary, summary.join('\n')).toEqual([]);
    });
  }
}
