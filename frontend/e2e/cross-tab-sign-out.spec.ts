import { expect, test } from "@playwright/test";

const account = { isAdministrator: true, canCreateReferrals: true,
  providerPublicId: null, patientPublicId: null, memberships: [] };
const referral = { publicId: "11111111-2222-4333-8444-555555555555",
  patientFirstName: "Alex", patientLastName: "Morgan", serviceName: "Physical exam",
  clinicName: "Northside Family Clinic", status: "PendingReview", stage: "PendingReview",
  createdAtUtc: "2026-11-12T14:00:00Z" };

test("sign-out clears an open tab without navigating it or rebroadcasting the signal", async ({ page, context }) => {
  await context.addInitScript(() => {
    const setItem = Storage.prototype.setItem;
    Storage.prototype.setItem = function (key: string, value: string) {
      if (this === localStorage && key === "clinic-session:sign-out") {
        const count = Number(sessionStorage.getItem("clinic-test:signals-sent") ?? "0");
        setItem.call(sessionStorage, "clinic-test:signals-sent", String(count + 1));
      }
      setItem.call(this, key, value);
    };
  });
  await context.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ json: account });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    if (path === "/api/referrals/search") return route.fulfill({ json: {
      items: [referral], totalCount: 1, page: 1, pageSize: 10, hasMore: false,
    } });
    return route.fulfill({ status: 500, json: {} });
  });
  const other = await context.newPage();
  await page.goto("/referrals");
  await other.goto("/referrals");
  await expect(page.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toBeVisible();
  await expect(other.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toBeVisible();
  const navigations: string[] = [];
  other.on("framenavigated", frame => { if (frame === other.mainFrame()) navigations.push(frame.url()); });

  // Unrelated storage writes must not sign out the receiving tab.
  await page.evaluate(() => localStorage.setItem("clinic-test:unrelated", "changed"));
  await expect(other.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toBeVisible();

  await page.getByLabel("Account: Demo Administrator", { exact: true }).click();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(page.getByRole("button", { name: "Continue with Google", exact: true })).toBeVisible();
  await expect(other.getByRole("button", { name: "Continue with Google", exact: true })).toBeVisible();
  await expect(other.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toHaveCount(0);
  await expect(other.getByRole("link", { name: "Patients", exact: true })).toHaveCount(0);
  expect(await other.evaluate(() => JSON.parse(sessionStorage.getItem("clinic-test:logout-options")!)))
    .toEqual({ openUrl: false });
  expect(navigations).toEqual([]);
  expect(other.url()).toContain("/referrals");
  const firstSignal = await page.evaluate(() => localStorage.getItem("clinic-session:sign-out"));
  expect(firstSignal).toBeTruthy();
  expect(await other.evaluate(() => localStorage.getItem("clinic-session:sign-out"))).toBe(firstSignal);
  expect(await page.evaluate(() => sessionStorage.getItem("clinic-test:signals-sent"))).toBe("1");
  expect(await other.evaluate(() => sessionStorage.getItem("clinic-test:signals-sent"))).toBeNull();

  await other.reload();
  await expect(other.getByRole("button", { name: "Continue with Google", exact: true })).toBeVisible();

  // A later sign-in can use the same tabs, and a second sign-out still notifies them.
  await page.getByRole("button", { name: "Continue with Google", exact: true }).click();
  await other.getByRole("button", { name: "Continue with Google", exact: true }).click();
  await expect(page.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toBeVisible();
  await expect(other.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toBeVisible();
  await page.getByLabel("Account: Demo Administrator", { exact: true }).click();
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(other.getByRole("button", { name: "Continue with Google", exact: true })).toBeVisible();
  expect(await other.evaluate(() => localStorage.getItem("clinic-session:sign-out"))).not.toBe(firstSignal);
  expect(await page.evaluate(() => sessionStorage.getItem("clinic-test:signals-sent"))).toBe("2");
  expect(await other.evaluate(() => sessionStorage.getItem("clinic-test:signals-sent"))).toBeNull();
});

test("a sign-out signal cancels a pending referral load and removes its cached view", async ({ page, context }) => {
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  let searches = 0;
  await context.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ json: account });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    if (path === "/api/referrals/search") {
      searches++;
      await pending;
      await route.fulfill({ json: { items: [referral], totalCount: 1, page: 1, pageSize: 10, hasMore: false } })
        .catch(() => { /* The receiving tab may already have aborted the request. */ });
      return;
    }
    return route.fulfill({ status: 500, json: {} });
  });
  const sender = await context.newPage();
  try {
    await page.goto("/referrals");
    await sender.goto("/referrals");
    await expect.poll(() => searches).toBe(2);
    await sender.getByLabel("Account: Demo Administrator", { exact: true }).click();
    await sender.getByRole("button", { name: "Sign out", exact: true }).click();
    await expect(page.getByRole("button", { name: "Continue with Google", exact: true })).toBeVisible();
    release();
    await expect(page.getByRole("link", { name: "View referral for Alex Morgan: Physical exam", exact: true })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Patients", exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => JSON.parse(sessionStorage.getItem("clinic-test:logout-options")!)))
      .toEqual({ openUrl: false });
  } finally { release(); }
});
