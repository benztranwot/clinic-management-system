import { expect, test, type Page } from "@playwright/test";

const clinic = { publicId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", name: "Cedar Family Health",
  address: "42 Cedar Lane", timeZoneId: "America/Chicago" };
const account = { isAdministrator: true, isVisitorWorkspace: true, canCreateReferrals: true,
  workspacePermissions: ["manage:clinics", "manage:patients", "manage:providers", "manage:services", "manage:referrals", "manage:provider-services"],
  providerPublicId: null, patientPublicId: null, memberships: [] };

async function emptyTokenScopes(page: Page) {
  await page.addInitScript(() => { (window as Window & { __testScopes?: string }).__testScopes = ""; });
}

test("verified visitors explicitly create one workspace with pending feedback and server-granted controls", async ({ page }) => {
  await emptyTokenScopes(page);
  let ready = false;
  let creates = 0;
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const unexpected: string[] = [];
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/me") return route.fulfill({ status: ready ? 200 : 403, json: ready ? account : {} });
    if (path === "/api/me/onboarding") return route.fulfill({ json: {
      status: "workspace_pending", canCreateWorkspace: true, email: "visitor@example.invalid",
    } });
    if (path === "/api/me/workspace" && request.method() === "POST") {
      creates++;
      expect(request.postData()).toBeNull();
      await gate;
      ready = true;
      return route.fulfill({ status: 201, json: { status: "ready" } });
    }
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    if (path === "/api/referrals/search") return route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, hasMore: false } });
    if (path === "/api/clinics/search") return route.fulfill({ json: { items: [clinic], page: 1, pageSize: 16, totalCount: 1, hasMore: false } });
    if (path === "/api/patients/search") return route.fulfill({ json: { items: [], page: 1, pageSize: 16, totalCount: 0, hasMore: false } });
    unexpected.push(request.method() + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  try {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Create your private workspace" })).toBeVisible();
    expect(creates).toBe(0); // Rendering and reloads must not create data.
    await page.reload();
    await page.getByRole("button", { name: "Create my workspace", exact: true }).click();
    const pending = page.getByRole("button", { name: "Creating workspace…", exact: true });
    await expect(pending).toBeDisabled();
    await expect(pending.locator(".loading-spinner")).toBeVisible();
    await expect(page.getByRole("status")).toContainText("Preparing your clinic records");
    expect(creates).toBe(1);
    release();
    await expect(page.getByRole("heading", { name: "Referral queue", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "New referral", exact: true })).toBeVisible();
    await page.getByRole("link", { name: "Clinics", exact: true }).click();
    await expect(page.getByRole("link", { name: "New clinic", exact: true })).toBeVisible();
    await page.getByRole("link", { name: "Patients", exact: true }).click();
    await expect(page.getByRole("link", { name: "Register patient", exact: true })).toBeVisible();
    expect(creates).toBe(1);
    expect(unexpected).toEqual([]);
  } finally { release(); }
});

test("failed workspace creation keeps a keyboard-accessible retry and never retries automatically", async ({ page }) => {
  await emptyTokenScopes(page);
  let attempts = 0;
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ status: 403, json: {} });
    if (path === "/api/me/onboarding") return route.fulfill({ json: { status: "workspace_pending", canCreateWorkspace: true } });
    if (path === "/api/me/workspace") {
      attempts++;
      return route.fulfill({ status: attempts === 1 ? 409 : 429, json: { message: attempts === 1
        ? "Another workspace is being prepared. Please try again shortly." : "Today's workspace limit has been reached. Please try again tomorrow." } });
    }
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/");
  const create = page.getByRole("button", { name: "Create my workspace", exact: true });
  await create.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("alert")).toContainText("Another workspace is being prepared");
  await expect(create).toBeEnabled();
  expect(attempts).toBe(1);
  await create.click();
  await expect(page.getByRole("alert")).toContainText("Today's workspace limit");
  expect(attempts).toBe(2);
  await expect(page.getByRole("link", { name: "Patients", exact: true })).toHaveCount(0);
});

test("an unverified visitor cannot create a workspace even if a response includes an enrollment flag", async ({ page }) => {
  let creates = 0;
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ status: 403, json: {} });
    if (path === "/api/me/onboarding") return route.fulfill({ json: { status: "email_confirmation_required",
      canCreateWorkspace: true, email: "visitor@example.invalid" } });
    creates++;
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Verify your email", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Create my workspace", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Check verification", exact: true }).click();
  expect(creates).toBe(0);
});
