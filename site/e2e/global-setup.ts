import fs from 'node:fs';
import path from 'node:path';

// The dev server compiles each route the first time it is requested. On CI runners that
// first compile can take longer than a test's navigation wait, so warm every exported
// route once before any test runs. Routes come from the static export produced by the
// build step; if the export is missing, warming is skipped.
const CONCURRENCY = 4;
const REQUEST_TIMEOUT_MS = 120_000;

function exportedRoutes(outDir: string): string[] {
  const routes: string[] = [];
  const walk = (dir: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name === 'index.html') {
        const rel = path.relative(outDir, dir).split(path.sep).join('/');
        routes.push(rel ? `/${rel}/` : '/');
      }
    }
  };
  walk(outDir);
  return routes.sort();
}

async function warm(baseURL: string, route: string): Promise<void> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    const response = await fetch(new URL(route, baseURL), { signal: controller.signal, redirect: 'follow' });
    await response.arrayBuffer();
  } catch (error) {
    console.warn(`[global-setup] could not warm ${route}: ${(error as Error).message}`);
  } finally {
    clearTimeout(timer);
  }
}

export default async function globalSetup(): Promise<void> {
  const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';
  const outDir = path.resolve(__dirname, '..', 'out');
  if (!fs.existsSync(outDir)) {
    console.warn('[global-setup] no static export found; skipping route warm-up');
    return;
  }
  const routes = exportedRoutes(outDir);
  const started = Date.now();
  let next = 0;
  const workers = Array.from({ length: CONCURRENCY }, async () => {
    while (next < routes.length) {
      const route = routes[next++];
      await warm(baseURL, route);
    }
  });
  await Promise.all(workers);
  console.log(`[global-setup] warmed ${routes.length} routes in ${((Date.now() - started) / 1000).toFixed(1)}s`);
}
