import { defineConfig, devices } from '@playwright/test';

// E2E against the deployed dev preview (the URL the PR links), not the dev server.
export default defineConfig({
  testDir: './e2e',
  testIgnore: ['**/signature.spec.ts'], // Storybook component test, not a static-site E2E
  timeout: 30000,
  use: {
    baseURL: 'http://130.61.208.211/',
  },
  projects: [
    {
      name: 'chromium-desktop',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
