import { expect, test, type Page } from "@playwright/test";
import type { AppointmentDetails, BookingOption } from "../src/api/appointments";

const clinicId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const providerId = "33333333-3333-4333-8333-333333333333";
const otherProviderId = "44444444-4444-4444-8444-444444444444";
const serviceId = "22222222-2222-4222-8222-222222222222";
const patientId = "11111111-1111-4111-8111-111111111111";
const appointmentId = "66666666-6666-4666-8666-666666666666";
const option: BookingOption = { clinicPublicId: clinicId, clinicName: "Northside Family Clinic", timeZoneId: "America/Chicago",
  clinicalServicePublicId: serviceId, serviceName: "Annual physical", providerPublicId: providerId, providerName: "Elizabeth Hart" };
const patient = { publicId: patientId, firstName: "Alex", lastName: "Morgan", dateOfBirth: "1990-03-05" };
const firstSlot = { startUtc: "2030-01-07T15:00:00Z", endUtc: "2030-01-07T15:30:00Z" };
const replacement = { startUtc: "2030-01-07T16:00:00Z", endUtc: "2030-01-07T16:30:00Z" };
const broadMatches = Array.from({ length: 16 }, (_, index) => ({
  ...patient, publicId: "12345678-1111-4111-8111-" + String(index).padStart(12, "0"), firstName: "Patient " + index,
}));

async function setup(page: Page) {
  const writes: unknown[] = [];
  const unexpected: string[] = [];
  const searches: unknown[] = [];
  let taken = false;
  let appointment: AppointmentDetails | undefined;
  await page.clock.setFixedTime(new Date("2030-01-06T18:00:00Z"));
  await page.route(url => url.pathname.startsWith("/api/"), async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === "/api/me") return route.fulfill({ json: { isAdministrator: false, canCreateReferrals: false,
      patientPublicId: null, providerPublicId: null, memberships: [{ clinicPublicId: clinicId, clinicName: option.clinicName, role: "FrontDesk" }] } });
    if (path === "/api/appointments/needs-attention/count") return route.fulfill({ json: { count: 0 } });
    if (path === "/api/appointments/upcoming/search") return route.fulfill({ json: {
      items: appointment ? [appointment] : [], page: 1, pageSize: 14, totalCount: appointment ? 1 : 0, hasMore: false } });
    if (path === "/api/appointments/booking-options") return route.fulfill({ json: [option,
      { ...option, providerPublicId: otherProviderId, providerName: "Jonathan Reed" }] });
    if (path === "/api/patients/search") {
      const search = request.postDataJSON();
      searches.push(search);
      const broad = search.lastName === "Morgan" && !search.firstName && !search.dateOfBirth;
      const hasMore = broad && search.page === 1;
      return route.fulfill({ json: {
        items: hasMore ? broadMatches : [patient], page: search.page, pageSize: 16,
        totalCount: broad ? 17 : 1, hasMore,
      } });
    }
    if (path === "/api/appointments/booking-availability"
      && [providerId, otherProviderId].includes(url.searchParams.get("providerPublicId") ?? "")
      && url.searchParams.get("clinicalServicePublicId") === serviceId) {
      return route.fulfill({ json: { timeZoneId: option.timeZoneId, days: [
        { date: "2030-01-07", status: "Available", slots: taken ? [replacement] : [firstSlot, replacement] },
        { date: "2030-01-08", status: "DayOff", slots: [] },
        { date: "2030-01-09", status: "FullyBooked", slots: [] },
        { date: "2030-01-12", status: "OutsideWorkingHours", slots: [] },
        { date: "2030-01-13", status: "OutsideWorkingHours", slots: [] },
      ] } });
    }
    if (request.method() === "POST" && path === "/api/appointments") {
      const input = request.postDataJSON();
      writes.push(input);
      if (input.startUtc === firstSlot.startUtc) {
        taken = true;
        return route.fulfill({ status: 409, json: { message: "This slot is no longer available. Choose another time." } });
      }
      appointment = { ...replacement, publicId: appointmentId, clinicPublicId: clinicId, clinicName: option.clinicName,
        timeZoneId: option.timeZoneId, patientName: "Alex Morgan", providerPublicId: providerId, providerName: option.providerName,
        serviceName: option.serviceName, referralPublicId: null, status: "Confirmed", canCancel: true, canComplete: false,
        canReschedule: true, overlapsProviderTimeOff: false };
      return route.fulfill({ status: 201, json: { publicId: appointmentId } });
    }
    if (path === `/api/appointments/${appointmentId}` && appointment) return route.fulfill({ json: appointment });
    unexpected.push(request.method() + " " + path);
    return route.fulfill({ status: 500, json: {} });
  });
  return { writes, unexpected, searches };
}

async function choose(page: Page) {
  await page.getByLabel("Service, clinic and provider", { exact: true }).selectOption(`${clinicId}:${serviceId}:${providerId}`);
  await page.getByText("Choose a patient", { exact: true }).click();
  await page.getByLabel("Date of birth", { exact: true }).fill(patient.dateOfBirth);
  await page.getByRole("button", { name: "Find patients" }).click();
  await page.getByRole("button", { name: "Alex Morgan · 1990-03-05" }).press("Enter");
  const calendar = page.getByRole("region", { name: "Appointment date", exact: true });
  await expect(calendar.getByRole("button", { name: /January 8, 2030, Day off/ })).toBeDisabled();
  await expect(calendar.getByRole("button", { name: /January 9, 2030, Fully booked/ })).toBeDisabled();
  await expect(calendar.getByRole("button", { name: /January 12, 2030, Outside working hours/ })).toBeDisabled();
  await expect(calendar.getByRole("button", { name: /January 13, 2030, Outside working hours/ })).toBeDisabled();
  await calendar.getByRole("button", { name: /January 7, 2030, Available/ }).click();
  await page.getByLabel("Appointment time", { exact: true }).selectOption(firstSlot.startUtc);
}

test("upcoming appointments supports direct booking and recovers from a taken slot", async ({ page }) => {
  const { writes, unexpected, searches } = await setup(page);
  await page.goto("/appointments/upcoming");
  await expect(page.getByRole("button", { name: "Refresh", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Create appointment", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Create appointment", exact: true })).toBeVisible();
  await choose(page);
  expect(searches).toEqual([{ firstName: "", lastName: "", dateOfBirth: "1990-03-05", page: 1 }]);
  await page.getByRole("button", { name: "Create appointment", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("This slot is no longer available. Choose another time.");
  await expect(page.getByLabel("Appointment time", { exact: true }).getByRole("option")).toHaveCount(2);
  await page.getByLabel("Appointment time", { exact: true }).selectOption(replacement.startUtc);
  await page.getByRole("button", { name: "Create appointment", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/appointments/${appointmentId}$`));
  await expect(page.getByText("Scheduled", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Refresh", exact: true })).toHaveCount(0);
  expect(writes).toEqual([firstSlot.startUtc, replacement.startUtc].map(startUtc => ({
    patientPublicId: patientId, providerPublicId: providerId, clinicalServicePublicId: serviceId, startUtc,
  })));
  await page.getByRole("link", { name: "Back to upcoming appointments", exact: true }).click();
  await expect(page.getByRole("listitem").filter({ hasText: "Alex Morgan" })).toBeVisible();
  expect(unexpected).toEqual([]);
});

test("changing provider clears the previous appointment date and time", async ({ page }) => {
  const { writes, unexpected } = await setup(page);
  await page.goto("/appointments/new");
  await choose(page);
  await expect(page.getByRole("button", { name: "Create appointment", exact: true })).toBeEnabled();
  await page.getByLabel("Service, clinic and provider", { exact: true }).selectOption(`${clinicId}:${serviceId}:${otherProviderId}`);
  const calendar = page.getByRole("region", { name: "Appointment date", exact: true });
  await expect(calendar.getByRole("button", { name: /January 7, 2030, Available/ })).toBeVisible();
  await expect(calendar.getByRole("button", { pressed: true })).toHaveCount(0);
  await expect(page.getByLabel("Appointment time", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create appointment", exact: true })).toBeDisabled();
  expect(writes).toEqual([]);
  expect(unexpected).toEqual([]);
});

test("patient lookup loads all name matches by scrolling one dropdown", async ({ page }) => {
  const { searches, unexpected } = await setup(page);
  await page.goto("/appointments/new");
  await page.getByText("Choose a patient", { exact: true }).click();
  await expect(page.getByText("Search by name or exact date of birth.")).toBeVisible();
  expect(searches).toEqual([]);
  await page.getByLabel("Last name", { exact: true }).fill("Morgan");
  await page.getByRole("button", { name: "Find patients" }).click();
  const results = page.getByRole("list", { name: "Matching patients" });
  await expect(results.getByRole("listitem")).toHaveCount(16);
  expect(searches).toHaveLength(1);
  await expect(page.getByRole("button", { name: "Show more" })).toHaveCount(0);
  await results.evaluate(list => { list.scrollTop = list.scrollHeight; });
  await expect(results.getByRole("listitem")).toHaveCount(17);
  await expect(page.getByRole("button", { name: "Previous", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Next", exact: true })).toHaveCount(0);
  await results.getByRole("button", { name: "Alex Morgan · 1990-03-05" }).click();
  await expect(results).toBeHidden();
  await expect(page.locator("summary").filter({ hasText: "Alex Morgan" })).toBeFocused();
  expect(searches).toEqual([1, 2].map(page => ({ firstName: "", lastName: "Morgan", page })));
  expect(unexpected).toEqual([]);
});
