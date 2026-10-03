import { defineConfig, devices } from '@playwright/test';

// E2E against the served static export (http.server on :80), not the dev server.
export default defineConfig({
  testDir: './e2e',
  timeout: 30000,
  use: {
    baseURL: 'http://localhost/',
  },
  projects: [
    {
      name: 'chromium-desktop',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
