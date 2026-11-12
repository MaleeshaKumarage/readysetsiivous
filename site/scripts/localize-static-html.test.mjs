import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { localizeHtmlLang, localizeOutput } from './localize-static-html.mjs';

const page = (lang) => `<!DOCTYPE html><html lang="${lang}" translate="no" class="notranslate"><head></head></html>`;

test('sets the lang attribute and keeps the rest of the tag', () => {
  assert.equal(localizeHtmlLang(page('fi'), 'en'), page('en'));
  assert.equal(localizeHtmlLang(page('fi'), 'sv'), page('sv'));
});

test('leaves html without a lang attribute unchanged', () => {
  const html = '<html><body></body></html>';
  assert.equal(localizeHtmlLang(html, 'en'), html);
});

test('localizes only the en and sv trees of the export', async () => {
  const out = await mkdtemp(join(tmpdir(), 'lang-'));
  for (const dir of ['en/varaus', 'sv', 'fi/terms']) await mkdir(join(out, dir), { recursive: true });
  await writeFile(join(out, 'en/varaus/index.html'), page('fi'));
  await writeFile(join(out, 'sv/index.html'), page('fi'));
  await writeFile(join(out, 'fi/terms/index.html'), page('fi'));
  const changed = await localizeOutput(out);
  assert.equal(changed, 2);
  assert.equal(await readFile(join(out, 'en/varaus/index.html'), 'utf8'), page('en'));
  assert.equal(await readFile(join(out, 'sv/index.html'), 'utf8'), page('sv'));
  assert.equal(await readFile(join(out, 'fi/terms/index.html'), 'utf8'), page('fi'));
});
