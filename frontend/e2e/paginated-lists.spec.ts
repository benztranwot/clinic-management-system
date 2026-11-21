import { expect, test, type Page } from "@playwright/test";
import type { ReferralAccount } from "../src/api/referrals";

const account: ReferralAccount = {
  isAdministrator: true, canCreateReferrals: true, memberships: [], providerPublicId: null, patientPublicId: null,
};
const id = (index: number) => `10000000-0000-4000-8000-${String(index).padStart(12, "0")}`;
const rows = Array.from({ length: 23 }, (_, i) => ({
  publicId: id(i + 1), firstName: `Patient ${i + 1}`, lastName: "Morgan", dateOfBirth: "1990-06-01",
  name: `Clinic ${i + 1}`, address: "1420 Willow Lane", timeZoneId: "America/Chicago",
  patientFirstName: `Patient ${i + 1}`, patientLastName: "Morgan", serviceName: "Consultation",
  clinicName: `Clinic ${i + 1}`, status: "PendingReview", stage: "PendingReview", createdAtUtc: "2026-11-20T14:00:00Z",
  patientName: `Patient ${i + 1} Morgan`, providerName: "Elizabeth Hart", clinicPublicId: id(80),
  startUtc: "2026-11-25T15:00:00Z", endUtc: "2026-11-25T15:30:00Z", referralPublicId: null,
}));

async function expectDesktopFit(page: Page) {
  const navigation = await page.getByRole("navigation", { name: "Main navigation" }).boundingBox();
  const main = await page.getByRole("main").boundingBox();
  expect(navigation && main && navigation.x + navigation.width <= main.x).toBeTruthy();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollHeight <= innerHeight + 1)).toBe(true);
  await expectPanelHeight(page, true);
}

async function expectPanelHeight(page: Page, full: boolean) {
  const gap = () => page.getByRole("main").evaluate(element => {
    const panel = element.querySelector(":scope > .workspace-panel")!;
    return element.getBoundingClientRect().bottom - parseFloat(getComputedStyle(element).paddingBottom)
      - panel.getBoundingClientRect().bottom;
  });
  if (full) await expect.poll(async () => Math.abs(await gap())).toBeLessThanOrEqual(1);
  else await expect.poll(gap).toBeGreaterThan(8);
}

async function mockLists(page: Page, patientCount = rows.length) {
  let patients = rows.slice(0, patientCount);
  const deletions: string[] = [];
  const requests: { path: string; body: Record<string, string | number> }[] = [];
  const unexpected: string[] = [];
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/me") return route.fulfill({ json: account });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 23 } });
    if (request.method() === "POST" && path.endsWith("/search")) {
      const body = request.postDataJSON();
      requests.push({ path, body });
      // A filtered match comes from outside the initial ten records.
      const term = body.firstName || body.name || body.patient;
      const source = path === "/api/patients/search" ? patients : rows;
      const filtered = term === "almost-full" ? source.slice(0, 15) : term ? source.filter(row => row.publicId === id(23)) : source;
      const pageSize = path.includes("/appointments/") ? 8 : path === "/api/referrals/search" ? 10 : 16;
      return route.fulfill({ json: { items: filtered.slice((body.page - 1) * pageSize, body.page * pageSize),
        page: body.page, pageSize, totalCount: filtered.length, hasMore: body.page * pageSize < filtered.length } });
    }
    if (path === `/api/patients/${id(17)}`) {
      if (request.method() === "GET") {
        const patient = patients.find(row => row.publicId === id(17));
        return patient ? route.fulfill({ json: patient }) : route.fulfill({ status: 404, json: {} });
      }
      if (request.method() === "DELETE") {
        deletions.push(id(17));
        patients = patients.filter(row => row.publicId !== id(17));
        return route.fulfill({ status: 204 });
      }
    }
    unexpected.push(request.method() + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  return { requests, unexpected, deletions };
}

for (const list of [
  { route: "/referrals", title: "Referral queue", pageSize: 10, field: "Patient", value: "Patient 23", key: "patient" },
  { route: "/clinics", title: "Clinics", pageSize: 16, field: "Clinic name", value: "Clinic 23", key: "name" },
  { route: "/patients", title: "Patients", pageSize: 16, field: "First name", value: "Patient 23", key: "firstName" },
]) {
  test(`${list.title}: page size, navigation and filtering reset the page`, async ({ page }) => {
    const { requests, unexpected } = await mockLists(page);
    await page.setViewportSize({ width: 1440, height: 1080 });
    await page.goto(list.route);
    const table = page.getByRole("table", { name: list.title });
    await expect(table.getByRole("row")).toHaveCount(list.pageSize + 1);
    await expect(page.getByRole("navigation", { name: /pages$/ })).toContainText(`Page 1 of ${Math.ceil(rows.length / list.pageSize)}`);
    await expectDesktopFit(page);
    if (list.route === "/referrals") {
      const filters = await page.getByRole("button", { name: "Filters", exact: true }).boundingBox();
      const create = await page.getByRole("link", { name: "New referral", exact: true }).boundingBox();
      expect(filters && create && create.x > filters.x).toBeTruthy();
    }
    for (let nextPage = 2; nextPage <= Math.ceil(rows.length / list.pageSize); nextPage++) {
      await page.getByRole("button", { name: "Next", exact: true }).click();
      await expect(page).toHaveURL(new RegExp(`page=${nextPage}$`));
      await expect.poll(() => requests.at(-1)?.body.page).toBe(nextPage);
      await expect(table.getByRole("row")).toHaveCount(Math.min(list.pageSize, rows.length - (nextPage - 1) * list.pageSize) + 1);
    }
    await expect(page.getByRole("button", { name: "Next", exact: true })).toBeDisabled();
    await expectPanelHeight(page, false);
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel(list.field, { exact: true }).fill(list.value);
    await dialog.getByRole("button", { name: "Apply filters" }).click();
    await expect(table.getByRole("row")).toHaveCount(2);
    await expectPanelHeight(page, false);
    await expect.poll(() => requests.at(-1)?.body[list.key]).toBe(list.value);
    expect(requests.at(-1)?.body.page).toBe(1);
    expect(page.url()).not.toContain("page=");
    expect(page.url()).not.toContain("Patient");
    await expect(page.getByRole("heading", { level: 1 })).toHaveCount(1);
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Clear filters" }).click();
    await expect(table.getByRole("row")).toHaveCount(list.pageSize + 1);
    if (list.pageSize === 16) {
      await page.getByRole("button", { name: "Filters", exact: true }).click();
      const boundaryFilters = page.getByRole("dialog");
      await boundaryFilters.getByLabel(list.field, { exact: true }).fill("almost-full");
      await boundaryFilters.getByRole("button", { name: "Apply filters" }).click();
      await expect(table.getByRole("row")).toHaveCount(16); // Fifteen records plus the table header.
      await expectPanelHeight(page, false);
      await page.getByRole("button", { name: "Filters", exact: true }).click();
      await page.getByRole("dialog").getByRole("button", { name: "Clear filters" }).click();
      await expect(table.getByRole("row")).toHaveCount(17);
      await expectPanelHeight(page, true);
    }
    expect(unexpected).toEqual([]);
  });
}

for (const view of ["upcoming", "availability-conflicts"]) {
  test(`${view}: paged cards, two desktop columns, and patient filter`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1080 });
    const { requests, unexpected } = await mockLists(page);
    await page.goto(`/appointments/${view}`);
    await expect(page.getByRole("button", { name: "Refresh", exact: true })).toHaveCount(0);
    await expect(page.getByRole("listitem")).toHaveCount(8);
    await expectDesktopFit(page);
    if (view === "upcoming") {
      const filters = await page.getByRole("button", { name: "Filters", exact: true }).boundingBox();
      const create = await page.getByRole("link", { name: "Create appointment", exact: true }).boundingBox();
      expect(filters && create && create.x > filters.x).toBeTruthy();
    }
    const columns = await page.locator(".appointment-grid").evaluate(el => getComputedStyle(el).gridTemplateColumns.split(" ").length);
    expect(columns).toBe(2);
    await page.getByRole("button", { name: "Next", exact: true }).click();
    await expect.poll(() => requests.at(-1)?.body.page).toBe(2);
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    await page.getByRole("dialog").getByLabel("Patient", { exact: true }).fill("Patient 23");
    await page.getByRole("button", { name: "Apply filters" }).click();
    await expect(page.getByRole("listitem")).toHaveCount(1);
    await expectPanelHeight(page, false);
    expect(page.url()).not.toContain("Patient");
    expect(requests.at(-1)?.body.page).toBe(1);
    await page.setViewportSize({ width: 390, height: 844 });
    const mobileColumns = await page.locator(".appointment-grid").evaluate(el => getComputedStyle(el).gridTemplateColumns.split(" ").length);
    expect(mobileColumns).toBe(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    if (view === "upcoming") {
      const menu = page.getByRole("button", { name: "Menu", exact: true });
      await menu.focus();
      await page.keyboard.press("Enter");
      await expect(menu).toHaveAttribute("aria-expanded", "true");
      await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Patients", exact: true }).click();
      await expect(page.getByRole("table", { name: "Patients" })).toBeVisible();
      await expect(menu).toHaveAttribute("aria-expanded", "false");
      const filters = page.getByRole("button", { name: "Filters", exact: true });
      await filters.focus();
      await page.keyboard.press("Enter");
      const dialog = page.getByRole("dialog", { name: "Filter patients" });
      await expect(dialog.getByLabel("First name", { exact: true })).toBeFocused();
      await page.keyboard.press("Escape");
      await expect(dialog).not.toBeVisible();
      await expect(filters).toBeFocused();
    }
    expect(unexpected).toEqual([]);
  });
}

test("patient details Back restores the selected page and filters", async ({ page }) => {
  const { requests, unexpected } = await mockLists(page);
  await page.goto("/patients");
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  await page.getByRole("dialog").getByLabel("Last name", { exact: true }).fill("Morgan");
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(page.getByRole("table", { name: "Patients" }).getByRole("row")).toHaveCount(17);
  await page.getByRole("button", { name: "Next", exact: true }).click();
  const patientLink = page.getByRole("link", { name: "View patient: Patient 17 Morgan", exact: true });
  await expect(patientLink).toBeVisible();
  await patientLink.click();
  await expect(page.getByRole("heading", { name: "Patient details" })).toBeVisible();
  await page.getByRole("link", { name: "Back to patients", exact: true }).click();
  await expect(page).toHaveURL(/patients\?page=2$/);
  await expect(patientLink).toBeVisible();
  expect(requests.at(-1)?.body.page).toBe(2);
  expect(requests.at(-1)?.body.lastName).toBe("Morgan");
  expect(unexpected).toEqual([]);
});

for (const patientCount of [23, 17]) {
  test(`patient deletion preserves page and filters with ${patientCount === 17 ? "an empty" : "a populated"} last page`, async ({ page }) => {
    await page.addInitScript(() => {
      (window as Window & { __testScopes?: string }).__testScopes = "manage:patients";
    });
    const { requests, unexpected, deletions } = await mockLists(page, patientCount);
    await page.goto("/patients");
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    const filters = page.getByRole("dialog", { name: "Filter patients" });
    await filters.getByLabel("Last name", { exact: true }).fill("Morgan");
    await filters.getByLabel("Date of birth", { exact: true }).fill("1990-06-01");
    await filters.getByRole("button", { name: "Apply filters" }).click();
    await expect(page.getByRole("table", { name: "Patients" }).getByRole("row")).toHaveCount(17);
    await page.getByRole("button", { name: "Next", exact: true }).click();
    await page.getByRole("link", { name: "View patient: Patient 17 Morgan", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Patient details", exact: true })).toBeVisible();
    page.once("dialog", dialog => dialog.accept());
    await page.getByRole("button", { name: "Delete patient", exact: true }).click();

    await expect(page).toHaveURL(/patients\?page=2$/);
    await expect(page.getByText("Patient deleted.", { exact: true })).toBeVisible();
    await expect.poll(() => requests.at(-1)?.body).toMatchObject({
      page: 2, firstName: "", lastName: "Morgan", dateOfBirth: "1990-06-01",
    });
    await expect(page.getByRole("button", { name: "Next", exact: true })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Previous", exact: true })).toBeEnabled();
    if (patientCount === 17) {
      await expect(page.getByText("No patients found.", { exact: true })).toBeVisible();
      await expect(page.getByRole("navigation", { name: "Patients pages" })).toContainText("No results on page 2");
    } else {
      await expect(page.getByRole("table", { name: "Patients" }).getByRole("row")).toHaveCount(7);
      await expect(page.getByRole("link", { name: "View patient: Patient 17 Morgan", exact: true })).toHaveCount(0);
    }

    await page.getByRole("button", { name: "Previous", exact: true }).click();
    await expect(page).toHaveURL(/\/patients$/);
    await expect(page.getByRole("table", { name: "Patients" }).getByRole("row")).toHaveCount(17);
    await expect.poll(() => requests.at(-1)?.body).toMatchObject({
      page: 1, firstName: "", lastName: "Morgan", dateOfBirth: "1990-06-01",
    });
    await page.getByRole("button", { name: "Filters", exact: true }).click();
    await expect(page.getByRole("dialog").getByLabel("Last name", { exact: true })).toHaveValue("Morgan");
    await expect(page.getByRole("dialog").getByLabel("Date of birth", { exact: true })).toHaveValue("1990-06-01");
    expect(deletions).toEqual([id(17)]);
    expect(unexpected).toEqual([]);
  });
}
