import { defineConfig, devices } from '@playwright/test';
if (process.env.AUDIENCE_TEST_CONTEXT_PATH) {
  for (const name of ['NEXT_PUBLIC_API_URL', 'NEXT_PUBLIC_KEYCLOAK_URL']) {
    const value = process.env[name];
    if (!value || !['127.0.0.1', 'localhost'].includes(new URL(value).hostname))
      throw new Error(`${name} must explicitly target the owned local/tunneled test services.`);
  }
}
export default defineConfig({
  testDir: './e2e', testMatch: '**/api-audience-live.spec.ts', timeout: 120000, workers: 1,
  use: { baseURL: 'http://127.0.0.1:5767', serviceWorkers: 'block', trace: 'off', screenshot: 'off', video: 'off' },
  projects: [{ name: 'real-keycloak', use: { ...devices['Desktop Chrome'] } }],
  webServer: { command: 'npm run dev -- --hostname 127.0.0.1 --port 5767', url: 'http://127.0.0.1:5767', reuseExistingServer: false, timeout: 120000 }
});
