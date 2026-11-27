import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  timeout: 45000,
  use: {
    baseURL: process.env.E2E_ORIGIN || "http://localhost:3000",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "admin-setup",
      testMatch: /admin\.setup\.ts$/,
      use: { ...devices["Desktop Chrome"] },
    },
    {
      name: "desktop",
      dependencies: ["admin-setup"],
      use: { ...devices["Desktop Chrome"] },
    },
    {
      name: "mobile",
      dependencies: ["admin-setup"],
      use: {
        ...devices["Pixel 7"],
        defaultBrowserType: "chromium",
        reducedMotion: "reduce",
      },
    },
  ],
});
