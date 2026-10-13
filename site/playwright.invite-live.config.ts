import { defineConfig, devices } from '@playwright/test';
export default defineConfig({ testDir: './e2e', testMatch: '**/employee-invite-live.spec.ts', timeout: 120000,
  use: { baseURL: 'http://127.0.0.1:5757', serviceWorkers: 'block' }, projects: [{ name: 'real-keycloak', use: { ...devices['Desktop Chrome'] } }],
  webServer: { command: 'npm run dev -- --hostname 127.0.0.1 --port 5757', url: 'http://127.0.0.1:5757', reuseExistingServer: false, timeout: 120000 } });
