import { expect, test } from "@playwright/test";

test("an unverified account cannot enter the workspace and can recheck provider verification", async ({ page }) => {
  let verified = false;
  const unexpected: string[] = [];
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() !== "GET") unexpected.push(route.request().method() + " " + path);
    if (path === "/api/me") return route.fulfill({ status: 403, json: {} });
    if (path === "/api/me/onboarding") return route.fulfill({ json: {
      status: verified ? "workspace_pending" : "email_confirmation_required", email: "alex@example.invalid",
    } });
    unexpected.push(path);
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/referrals");
  await expect(page.getByRole("heading", { name: "Verify your email" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Patients", exact: true })).toHaveCount(0);
  await expect(page.getByRole("textbox", { name: "Confirmation code" })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Verify your email" })).toBeVisible();
  verified = true;
  await page.getByRole("button", { name: "Check verification", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Email confirmed" })).toBeVisible();
  await expect(page.getByText(/waiting for workspace access/)).toBeVisible();
  expect(page.context().pages()).toHaveLength(1);
  expect(page.url()).toContain("/referrals");
  expect(unexpected).toEqual([]);
});

test("a trusted verified email skips the code form and exposes no staff controls", async ({ page }) => {
  const unexpected: string[] = [];
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() !== "GET") unexpected.push(route.request().method() + " " + path);
    if (path === "/api/me") return route.fulfill({ status: 403, json: {} });
    if (path === "/api/me/onboarding") return route.fulfill({ json: { status: "workspace_pending", email: "alex@example.invalid" } });
    unexpected.push(path);
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Email confirmed" })).toBeVisible();
  await page.getByRole("button", { name: "Check access", exact: true }).click();
  await expect(page.getByText(/waiting for workspace access/)).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Confirmation code" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Patients", exact: true })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Referrals", exact: true })).toHaveCount(0);
  expect(unexpected).toEqual([]);
});

test("provider status failures retain a retry action with pending feedback", async ({ page }) => {
  let reads = 0;
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ status: 403, json: {} });
    if (path === "/api/me/onboarding") {
      reads++;
      if (reads === 1) return route.fulfill({ status: 503, json: { message: "Could not check your email status." } });
      await gate;
      return route.fulfill({ json: { status: "workspace_pending", email: "alex@example.invalid" } });
    }
    return route.fulfill({ status: 500, json: {} });
  });
  try {
    await page.goto("/");
    await expect(page.getByRole("alert")).toHaveText("Could not check your email status.");
    await page.getByRole("button", { name: "Retry", exact: true }).click();
    await expect(page.getByRole("status")).toHaveText("Checking your email status…");
    release();
    await expect(page.getByRole("heading", { name: "Email confirmed" })).toBeVisible();
  } finally { release(); }
});
