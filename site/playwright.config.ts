import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: ['**/company-integrity.spec.ts', '**/public-accessibility.spec.ts', '**/admin.spec.ts', '**/admin-scope.spec.ts', '**/manager.spec.ts', '**/shift-reactivation.spec.ts', '**/shift-assignment.spec.ts', '**/quality-cycle.spec.ts', '**/my-shifts.spec.ts', '**/booking-availability.spec.ts', '**/employee-invite.spec.ts', '**/entertainment*.spec.ts'],
  timeout: 30000,
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
