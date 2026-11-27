import { expect, test, type Page, type Route } from "@playwright/test";
import type { Clinic } from "../src/api/clinics";
import type { PatientDetails } from "../src/api/patients";

const clinicId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const patientId = "11111111-1111-4111-8111-111111111111";
const providerId = "33333333-3333-4333-8333-333333333333";
const service = { publicId: "22222222-2222-4222-8222-222222222222", name: "Annual physical" };
const clinic: Clinic = { publicId: clinicId, name: "Willow Family Clinic", address: "42 Willow Lane", timeZoneId: "America/Chicago" };
const provider = { publicId: providerId, firstName: "Elizabeth", lastName: "Hart" };
const account = { isAdministrator: true, canCreateReferrals: true, providerPublicId: null, patientPublicId: null, memberships: [] };

async function setup(page: Page, scopes: string, handle: (route: Route, path: string) => Promise<unknown>) {
  await page.addInitScript(value => {
    (window as Window & { __testScopes?: string }).__testScopes = value;
  }, scopes);
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/me") return route.fulfill({ json: account });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    await handle(route, path);
  });
}

test("clinic creation validates input, shows pending feedback, and safely recovers from an edit conflict", async ({ page }) => {
  let saved: Clinic | undefined;
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const writes: { method: string; body: Record<string, unknown> }[] = [];
  const unexpected: string[] = [];
  let conflict = true;
  await setup(page, "manage:clinics", async (route, path) => {
    const method = route.request().method();
    if (path === "/api/clinics/search") return route.fulfill({ json: {
      items: saved ? [saved] : [], page: 1, pageSize: 16, totalCount: saved ? 1 : 0, hasMore: false,
    } });
    if (path === "/api/clinics" && method === "POST") {
      const body = route.request().postDataJSON();
      writes.push({ method, body });
      await gate;
      saved = { publicId: clinicId, ...body };
      return route.fulfill({ status: 201, json: saved });
    }
    if (path === `/api/clinics/${clinicId}` && method === "PUT") {
      const body = route.request().postDataJSON();
      writes.push({ method, body });
      if (conflict) {
        conflict = false;
        saved = { ...saved!, address: "99 Oak Road", timeZoneId: "America/Denver" };
        return route.fulfill({ status: 409, json: {} });
      }
      saved = { ...saved!, ...body };
      return route.fulfill({ json: saved });
    }
    if (path === `/api/clinics/${clinicId}`) return route.fulfill({ json: saved });
    if ([`/api/clinics/${clinicId}/providers`, `/api/clinics/${clinicId}/services`].includes(path)) return route.fulfill({ json: [] });
    unexpected.push(method + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  try {
    await page.goto("/clinics");
    await page.getByRole("link", { name: "New clinic", exact: true }).click();
    await page.getByLabel("Clinic name", { exact: true }).fill("   ");
    await page.getByLabel("Address", { exact: true }).fill(clinic.address);
    await page.getByLabel("Time zone", { exact: true }).fill(clinic.timeZoneId);
    await page.getByRole("button", { name: "Save clinic", exact: true }).click();
    await expect(page.getByRole("alert")).toHaveText("Enter a clinic name, address, and time zone.");
    expect(writes).toHaveLength(0);
    await page.getByLabel("Clinic name", { exact: true }).fill("  " + clinic.name + "  ");
    await page.getByRole("button", { name: "Save clinic", exact: true }).click();
    const saving = page.getByRole("button", { name: "Saving…", exact: true });
    await expect(saving).toBeDisabled();
    await expect(saving.locator(".loading-spinner")).toBeVisible();
    await expect(page.getByLabel("Clinic name", { exact: true })).toBeDisabled();
    await expect.poll(() => writes.length).toBe(1);
    release();
    await expect(page.getByRole("heading", { name: clinic.name, exact: true })).toBeVisible();
    await page.getByRole("link", { name: "Edit clinic", exact: true }).click();
    await page.getByLabel("Address", { exact: true }).fill("18 Maple Avenue");
    await page.getByRole("button", { name: "Save clinic", exact: true }).click();
    await expect(page.getByRole("alert")).toContainText("scheduling settings changed");
    expect(writes).toHaveLength(2);
    expect(writes[1].body).toMatchObject({ expectedAddress: clinic.address, expectedTimeZoneId: clinic.timeZoneId });
    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(page.getByText("99 Oak Road", { exact: true })).toBeVisible();
    await page.getByRole("link", { name: "Edit clinic", exact: true }).click();
    await expect(page.getByLabel("Address", { exact: true })).toHaveValue("99 Oak Road");
    await expect(page.getByLabel("Time zone", { exact: true })).toHaveValue("America/Denver");
    await page.getByLabel("Clinic name", { exact: true }).fill("Willow Community Clinic");
    await page.getByRole("button", { name: "Save clinic", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Willow Community Clinic", exact: true })).toBeVisible();
    expect(writes[2].body).toEqual({ name: "Willow Community Clinic" });
    await expect(page.getByText("99 Oak Road", { exact: true })).toBeVisible();
    expect(unexpected).toEqual([]);
  } finally { release(); }
});

test("patient registration and editing recover from validation errors and update the directory", async ({ page }) => {
  let saved: PatientDetails | undefined;
  const writes: { method: string; body: Record<string, unknown> }[] = [];
  const unexpected: string[] = [];
  await setup(page, "manage:patients", async (route, path) => {
    const method = route.request().method();
    if (path === "/api/patients/search") return route.fulfill({ json: {
      items: saved ? [saved] : [], page: 1, pageSize: 16, totalCount: saved ? 1 : 0, hasMore: false,
    } });
    if ((path === "/api/patients" && method === "POST") || (path === `/api/patients/${patientId}` && method === "PUT")) {
      const body = route.request().postDataJSON();
      writes.push({ method, body });
      if (body.dateOfBirth === "2099-01-01") return route.fulfill({ status: 400, json: {} });
      saved = { publicId: patientId, ...body };
      return route.fulfill({ status: method === "POST" ? 201 : 204 });
    }
    if (path === `/api/patients/${patientId}` && method === "GET") return route.fulfill({ json: saved });
    unexpected.push(method + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/patients");
  await page.getByRole("link", { name: "Register patient", exact: true }).click();
  await page.getByLabel("First name", { exact: true }).fill("   ");
  await page.getByLabel("Last name", { exact: true }).fill("Morgan");
  await page.getByLabel("Date of birth", { exact: true }).fill("2099-01-01");
  await page.getByRole("button", { name: "Register patient", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("Enter a first name and a last name.");
  expect(writes).toHaveLength(0);
  await page.getByLabel("First name", { exact: true }).fill("  Alex  ");
  await page.getByRole("button", { name: "Register patient", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("date of birth on or before today");
  await expect(page.getByLabel("First name", { exact: true })).toHaveValue("  Alex  ");
  expect(writes).toHaveLength(1);
  await page.getByLabel("Date of birth", { exact: true }).fill("1990-03-05");
  await page.getByRole("button", { name: "Register patient", exact: true }).click();
  await page.getByRole("link", { name: "View patient: Alex Morgan", exact: true }).click();
  await page.getByRole("link", { name: "Edit patient", exact: true }).click();
  await expect(page.getByLabel("First name", { exact: true })).toHaveValue("Alex");
  await page.getByLabel("Last name", { exact: true }).fill("Reed");
  await page.getByRole("button", { name: "Save changes", exact: true }).click();
  await expect(page.getByRole("definition").filter({ hasText: /^Alex Reed$/ })).toBeVisible();
  await page.getByRole("link", { name: "Back to patients", exact: true }).click();
  await expect(page.getByRole("link", { name: "View patient: Alex Reed", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "View patient: Alex Morgan", exact: true })).toHaveCount(0);
  expect(writes.map(write => write.method)).toEqual(["POST", "POST", "PUT"]);
  expect(writes[2].body).toEqual({ firstName: "Alex", lastName: "Reed", dateOfBirth: "1990-03-05" });
  expect(unexpected).toEqual([]);
});

test("provider service assignment refreshes options and preserves a referenced assignment on conflict", async ({ page }) => {
  let assigned = false;
  let removals = 0;
  const writes: string[] = [];
  const unexpected: string[] = [];
  await setup(page, "manage:providers manage:provider-services", async (route, path) => {
    const method = route.request().method();
    if (path === `/api/clinics/${clinicId}`) return route.fulfill({ json: clinic });
    if (path === `/api/clinics/${clinicId}/providers`) return route.fulfill({ json: [provider] });
    if (path === `/api/providers/${providerId}`) return route.fulfill({ json: provider });
    if (path === `/api/clinics/${clinicId}/services`) return route.fulfill({ json: [service] });
    if (path === `/api/providers/${providerId}/services` && method === "GET") return route.fulfill({ json: assigned ? [service] : [] });
    if (path === `/api/providers/${providerId}/services` && method === "POST") {
      writes.push(method);
      expect(route.request().postDataJSON()).toEqual({ clinicalServicePublicId: service.publicId });
      assigned = true;
      return route.fulfill({ status: 201 });
    }
    if (path === `/api/providers/${providerId}/services/${service.publicId}` && method === "DELETE") {
      writes.push(method);
      removals++;
      if (removals === 1) return route.fulfill({ status: 409, json: {} });
      assigned = false;
      return route.fulfill({ status: 204 });
    }
    unexpected.push(method + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto(`/clinics/${clinicId}/providers/${providerId}`);
  await page.getByRole("combobox", { name: "Service to assign", exact: true }).selectOption(service.publicId);
  await page.getByRole("button", { name: "Assign service", exact: true }).click();
  const assignments = page.getByRole("list", { name: "Assigned services", exact: true });
  await expect(assignments).toContainText(service.name);
  await expect(page.getByText("No unassigned services at this clinic.", { exact: true })).toBeVisible();
  page.on("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Remove Annual physical assignment", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("This assignment is used by a referral and cannot be removed.");
  await expect(assignments).toContainText(service.name);
  expect(removals).toBe(1);
  await page.getByRole("button", { name: "Remove Annual physical assignment", exact: true }).click();
  await expect(page.getByText("No services assigned.", { exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Service to assign", exact: true }).getByRole("option", { name: service.name, exact: true })).toHaveCount(1);
  expect(writes).toEqual(["POST", "DELETE", "DELETE"]);
  expect(unexpected).toEqual([]);
});

test("read-only directory access hides writes and denies direct edit routes", async ({ page }) => {
  const unexpected: string[] = [];
  await setup(page, "", async (route, path) => {
    if (route.request().method() !== "GET" && path !== "/api/clinics/search" && path !== "/api/patients/search") {
      unexpected.push(route.request().method() + " " + path);
      return route.fulfill({ status: 500, json: {} });
    }
    if (path === "/api/clinics/search") return route.fulfill({ json: {
      items: [clinic], page: 1, pageSize: 16, totalCount: 1, hasMore: false,
    } });
    if (path === "/api/patients/search") return route.fulfill({ json: {
      items: [], page: 1, pageSize: 16, totalCount: 0, hasMore: false,
    } });
    if (path === `/api/clinics/${clinicId}`) return route.fulfill({ json: clinic });
    if (path === `/api/clinics/${clinicId}/providers`) return route.fulfill({ json: [provider] });
    if (path === `/api/providers/${providerId}`) return route.fulfill({ json: provider });
    if (path === `/api/providers/${providerId}/services`) return route.fulfill({ json: [service] });
    if (path === `/api/clinics/${clinicId}/services`) return route.fulfill({ json: [service] });
    if (path === `/api/services/${service.publicId}`) return route.fulfill({ json: service });
    unexpected.push(route.request().method() + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  await page.goto("/clinics");
  await expect(page.getByRole("table", { name: "Clinics" })).toBeVisible();
  await expect(page.getByRole("link", { name: "New clinic", exact: true })).toHaveCount(0);
  await page.goto(`/clinics/${clinicId}/edit`);
  await expect(page.getByRole("alert")).toContainText("permission to edit clinics");
  await expect(page.getByRole("button", { name: "Save clinic", exact: true })).toHaveCount(0);
  await page.goto("/patients/new");
  await expect(page.getByRole("alert")).toContainText("permission to create patients");
  await expect(page.getByRole("button", { name: "Register patient", exact: true })).toHaveCount(0);
  await page.goto(`/clinics/${clinicId}/providers/${providerId}`);
  await expect(page.getByRole("list", { name: "Assigned services", exact: true })).toContainText(service.name);
  await expect(page.getByRole("button", { name: /Remove .* assignment/ })).toHaveCount(0);
  await expect(page.getByRole("combobox", { name: "Service to assign", exact: true })).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`/clinics/${clinicId}/services/${service.publicId}`);
  await expect(page.getByRole("heading", { name: "Service details" })).toBeVisible();
  await expect(page.getByRole("definition").filter({ hasText: service.name })).toBeVisible();
  await expect(page.getByRole("definition").filter({ hasText: clinic.name })).toBeVisible();
  await expect(page.getByRole("region", { name: "Service record" })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  expect(unexpected).toEqual([]);
});
