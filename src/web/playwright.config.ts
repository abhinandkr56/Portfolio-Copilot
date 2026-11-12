import { defineConfig, devices } from "@playwright/test";

// Run with the AppHost already running in mock mode (doc 07): `dotnet run --project src/PortfolioCopilot.AppHost`.
export default defineConfig({
  testDir: "e2e",
  timeout: 60_000,
  retries: 0,
  reporter: [["list"]],
  use: { baseURL: "http://127.0.0.1:3000", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
