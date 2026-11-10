import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  // Discover new local browser suites automatically; a hand-maintained allowlist
  // previously left admin-edit-inactive.spec.ts out of CI.
  testMatch: '**/*.spec.ts',
  // These suites were already outside the default selection and require
  // owned live-service fixtures, a seeded dev API, or Storybook.
  testIgnore: ['**/api-audience-live.spec.ts', '**/employee-invite-live.spec.ts', '**/api-booking.spec.ts', '**/signature.spec.ts'],
  timeout: 30000,
  // Compiles every exported route once before tests run (see e2e/global-setup.ts).
  globalSetup: './e2e/global-setup.ts',
  // CI shards the suite across runners and merges the blob reports afterwards.
  reporter: process.env.CI ? [['blob', { outputDir: 'blob-report' }], ['line']] : 'list',
  use: {
    baseURL: 'http://localhost:3000',
    serviceWorkers: 'block',
  },
  projects: [
    {
      name: 'chromium-desktop',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mobile-touch',
      use: { ...devices['Pixel 5'] },
    },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI,
    timeout: 120 * 1000,
  },
});
