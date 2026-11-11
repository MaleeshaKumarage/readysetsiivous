import fs from 'node:fs';
import path from 'node:path';
import type { FullConfig } from '@playwright/test';

// The dev server compiles each route the first time it is requested. On CI runners that
// first compile can take longer than a test's navigation wait, so warm every exported
// route once before any test runs. Routes come from the static export produced by the
// build step; if the export is missing, warming is skipped. The base URL comes from the
// Playwright config, and the setup fails if no route responds, so a wrong URL cannot
// pass silently.
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

async function warm(baseURL: string, route: string): Promise<boolean> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    // Any response means the server answered and the route was compiled (the 404 page is a valid response).
    const response = await fetch(new URL(route, baseURL), { signal: controller.signal, redirect: 'follow' });
    await response.arrayBuffer();
    return true;
  } catch (error) {
    console.warn(`[global-setup] could not warm ${route}: ${(error as Error).message}`);
    return false;
  } finally {
    clearTimeout(timer);
  }
}

export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL;
  if (!baseURL) throw new Error('[global-setup] no baseURL is configured for the Playwright projects');
  const outDir = path.resolve(__dirname, '..', 'out');
  if (!fs.existsSync(outDir)) {
    console.warn('[global-setup] no static export found; skipping route warm-up');
    return;
  }
  const routes = exportedRoutes(outDir);
  if (routes.length === 0) {
    console.warn('[global-setup] static export has no routes; skipping route warm-up');
    return;
  }
  const started = Date.now();
  let next = 0;
  let answered = 0;
  const workers = Array.from({ length: CONCURRENCY }, async () => {
    while (next < routes.length) {
      const route = routes[next++];
      if (await warm(baseURL, route)) answered++;
    }
  });
  await Promise.all(workers);
  if (answered === 0) {
    throw new Error(`[global-setup] no route answered at ${baseURL}; check the web server`);
  }
  console.log(`[global-setup] warmed ${answered}/${routes.length} routes at ${baseURL} in ${((Date.now() - started) / 1000).toFixed(1)}s`);
}
