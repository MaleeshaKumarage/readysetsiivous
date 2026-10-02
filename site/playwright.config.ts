import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 30000,
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000',
    // Keycloak login redirects cross-origin; give the flow room to complete.
    navigationTimeout: 30000,
  },
  webServer: {
    command: process.env.CI ? 'npm run build && npm run start' : 'npm run dev',
    url: 'http://localhost:3000/fi',
    reuseExistingServer: !process.env.CI,
    timeout: 120000,
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
});
