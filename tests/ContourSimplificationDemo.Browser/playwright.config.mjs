import { defineConfig } from "@playwright/test";
import { fileURLToPath } from "node:url";

const baseURL = "http://127.0.0.1:5091";

export default defineConfig({
  testDir: ".",
  testMatch: "demo.spec.mjs",
  outputDir: "../../artifacts/contour-demo-browser",
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  reporter: "list",
  use: { baseURL, trace: "retain-on-failure" },
  projects: [
    { name: "chromium", use: { browserName: "chromium" } },
    { name: "edge", use: { browserName: "chromium", channel: "msedge" } }
  ],
  webServer: {
    command: `dotnet run --project examples/ContourSimplificationDemo -c Release --no-build -- --urls ${baseURL}`,
    cwd: fileURLToPath(new URL("../../", import.meta.url)),
    url: baseURL,
    reuseExistingServer: false,
    timeout: 30_000
  }
});
