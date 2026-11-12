// Post-build step for the static export. The root layout renders <html lang="fi"> for every
// route, so the prerendered /en/ and /sv/ pages would tell crawlers they are Finnish. This
// rewrites the lang attribute of each prerendered file under out/en and out/sv.
import { readdir, readFile, writeFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const LOCALIZED = ['en', 'sv'];

export function localizeHtmlLang(html, lang) {
  return html.replace(/<html lang="[a-zA-Z-]+"/, `<html lang="${lang}"`);
}

async function htmlFiles(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  const files = await Promise.all(entries.map((entry) => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? htmlFiles(path) : entry.name.endsWith('.html') ? [path] : [];
  }));
  return files.flat();
}

export async function localizeOutput(outDir) {
  let changed = 0;
  for (const lang of LOCALIZED) {
    let files;
    try {
      files = await htmlFiles(join(outDir, lang));
    } catch (error) {
      if (error.code === 'ENOENT') continue;
      throw error;
    }
    for (const file of files) {
      const html = await readFile(file, 'utf8');
      const next = localizeHtmlLang(html, lang);
      if (next !== html) {
        await writeFile(file, next);
        changed += 1;
      }
    }
  }
  return changed;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const outDir = fileURLToPath(new URL('../out/', import.meta.url));
  const changed = await localizeOutput(outDir);
  console.log(`localize-static-html: set lang on ${changed} file(s) under ${relative(process.cwd(), outDir) || outDir}`);
}
