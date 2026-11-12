import { expect, test } from "@playwright/test";

const API = "http://127.0.0.1:5080";

test("connect, see the demo findings, see the holdings", async ({ page, request }) => {
  // Start from the demo scenario and no session.
  expect((await request.post(`${API}/api/dev/scenario/demo`)).ok()).toBe(true);
  expect((await request.post(`${API}/api/session/logout`)).status()).toBe(204);

  // 1. Not connected → the Connect banner.
  await page.goto("/");
  await expect(page.getByText("Connect your Zerodha account to start.")).toBeVisible();

  // 2. Connect (mock login round trip) → back on the dashboard with the 8 demo findings, Equity first.
  await page.getByRole("link", { name: "Connect Zerodha" }).click();
  await page.waitForURL("http://127.0.0.1:3000/**");
  await expect(page.getByRole("status").filter({ hasText: "Connected" })).toBeVisible();
  const cards = page.getByTestId("finding-card");
  await expect(cards).toHaveCount(8);
  await expect(cards.first()).toContainText("Equity");

  // 3. Holdings: an INFY row, and the Regular badge on the Alpha Regular fund.
  await page.goto("/holdings");
  await expect(page.getByRole("row", { name: /INFY/ })).toBeVisible();
  const alpha = page.getByRole("row", { name: /Alpha Flexi Cap Fund - Regular Plan - Growth/ });
  await expect(alpha.getByText("Regular", { exact: true })).toBeVisible();
});
