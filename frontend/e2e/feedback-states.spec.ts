import { expect, test, type Page } from "@playwright/test";


async function expectCompactPanel(page: Page) {
  const gap = await page.getByRole("main").evaluate(element => {
    const panel = element.querySelector(":scope > .workspace-panel")!;
    return element.getBoundingClientRect().bottom - parseFloat(getComputedStyle(element).paddingBottom) - panel.getBoundingClientRect().bottom;
  });
  expect(gap).toBeGreaterThan(8);
}

const account = { isAdministrator: true, canCreateReferrals: true, providerPublicId: null, patientPublicId: null, memberships: [] };

test("mobile navigation returns focus on Escape and moves it to content after navigation", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route(url => url.pathname.startsWith("/api/"), route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") {
      return route.fulfill({ json: { ...account, isAdministrator: false, canCreateReferrals: false,
        patientPublicId: "11111111-1111-4111-8111-111111111111" } });
    }
    if (["/api/me/referrals", "/api/me/appointments"].includes(path)) return route.fulfill({ json: [] });
    return route.fulfill({ status: 404 });
  });

  await page.goto("/my-referrals");
  const menu = page.getByRole("button", { name: "Menu", exact: true });
  const appointments = page.getByRole("link", { name: "My appointments", exact: true });
  await expect(menu).toBeVisible();
  await menu.press("Enter");
  await appointments.focus();
  await appointments.press("Escape");
  await expect(menu).toHaveAttribute("aria-expanded", "false");
  await expect(menu).toBeFocused();

  await menu.press("Enter");
  await appointments.focus();
  await appointments.press("Enter");
  await expect(page.getByRole("heading", { name: "My appointments", exact: true })).toBeVisible();
  await expect(menu).toHaveAttribute("aria-expanded", "false");
  await expect(page.getByRole("main")).toBeFocused();
});

test("a loading spinner respects reduced motion and a failed referral queue can recover", async ({ page }) => {
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  let queueReads = 0;
  const unexpected: string[] = [];
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ json: account });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    if (path === "/api/referrals/search") {
      queueReads++;
      if (queueReads === 1) {
        await gate;
        return route.fulfill({ status: 503, json: { message: "Temporarily unavailable." } });
      }
      return route.fulfill({ json: { items: [], page: 1, pageSize: 10, totalCount: 0, hasMore: false } });
    }
    unexpected.push(route.request().method() + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  try {
    await page.goto("/referrals");
    const loading = page.getByRole("status").filter({ hasText: "Loading referrals" });
    await expect(loading).toBeVisible();
    const spinner = loading.locator(".loading-spinner");
    await expect(spinner).toHaveAttribute("aria-hidden", "true");
    expect(await spinner.evaluate(element => getComputedStyle(element).animationName)).toBe("none");
    release();
    await expect(page.getByRole("alert")).toHaveText("Unable to load referrals.");
    await expectCompactPanel(page);
    await expect(loading).toHaveCount(0);
    await page.getByRole("button", { name: "Try again", exact: true }).click();
    await expect(page.getByText("No referrals on this page.", { exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Referral pages" })).toContainText("0 results");
    await expect(page.getByRole("alert")).toHaveCount(0);
    await expectCompactPanel(page);
    expect(queueReads).toBe(2);
    expect(unexpected).toEqual([]);
  } finally { release(); }
});

test("server-denied patient and booking views do not expose records or creation controls", async ({ page }) => {
  const unexpected: string[] = [];
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ json: account });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    if (["/api/patients/search", "/api/appointments/booking-options"].includes(path)) return route.fulfill({ status: 403, json: {} });
    unexpected.push(route.request().method() + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/patients");
  await expect(page.getByRole("alert")).toContainText("permission to view patients");
  await expect(page.getByRole("table")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Filters", exact: true })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Register patient", exact: true })).toHaveCount(0);
  await page.goto("/appointments/new");
  await expect(page.getByRole("alert")).toContainText("access");
  await expect(page.getByText("Choose a patient", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create appointment", exact: true })).toHaveCount(0);
  expect(unexpected).toEqual([]);
});
