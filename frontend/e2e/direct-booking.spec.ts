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

async function setup(page: Page) {
  const writes: unknown[] = [];
  const unexpected: string[] = [];
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
      items: appointment ? [appointment] : [], page: 1, pageSize: 8, totalCount: appointment ? 1 : 0, hasMore: false } });
    if (path === "/api/appointments/booking-options") return route.fulfill({ json: [option,
      { ...option, providerPublicId: otherProviderId, providerName: "Jonathan Reed" }] });
    if (path === "/api/patients/search") return route.fulfill({ json: { items: [patient], page: 1, pageSize: 16, totalCount: 1, hasMore: false } });
    if (path === `/api/providers/${providerId}/availability` || path === `/api/providers/${otherProviderId}/availability`) {
      return route.fulfill({ json: { date: url.searchParams.get("date"), timeZoneId: option.timeZoneId, slots: taken ? [replacement] : [firstSlot, replacement] } });
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
  return { writes, unexpected };
}

async function choose(page: Page) {
  await page.getByLabel("Service, clinic and provider", { exact: true }).selectOption(`${clinicId}:${serviceId}:${providerId}`);
  await page.getByLabel("Patient", { exact: true }).selectOption(patientId);
  await page.getByLabel("Appointment date", { exact: true }).fill("2030-01-07");
  await page.getByLabel("Appointment time", { exact: true }).selectOption(firstSlot.startUtc);
}

test("upcoming appointments supports direct booking and recovers from a taken slot", async ({ page }) => {
  const { writes, unexpected } = await setup(page);
  await page.goto("/appointments/upcoming");
  await expect(page.getByRole("button", { name: "Refresh", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Create appointment", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Create appointment", exact: true })).toBeVisible();
  await choose(page);
  await page.getByRole("button", { name: "Create appointment", exact: true }).click();
  await expect(page.getByRole("alert")).toHaveText("This slot is no longer available. Choose another time.");
  await expect(page.getByLabel("Appointment time", { exact: true }).getByRole("option")).toHaveCount(2);
  await page.getByLabel("Appointment time", { exact: true }).selectOption(replacement.startUtc);
  await page.getByRole("button", { name: "Create appointment", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/appointments/${appointmentId}$`));
  await expect(page.getByText("Confirmed", { exact: true })).toBeVisible();
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
  await expect(page.getByLabel("Appointment date", { exact: true })).toHaveValue("");
  await expect(page.getByLabel("Appointment time", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Create appointment", exact: true })).toBeDisabled();
  expect(writes).toEqual([]);
  expect(unexpected).toEqual([]);
});
