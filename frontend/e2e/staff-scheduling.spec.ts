import { expect, test, type BrowserContext } from "@playwright/test";
import type { AppointmentDetails } from "../src/api/appointments";
import type { ReferralAccount, ReferralDetails, ReferralUpdate } from "../src/api/referrals";

const clinicId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const providerId = "33333333-3333-4333-8333-333333333333";
const referralId = "11111111-2222-4333-8444-555555555555";
const appointmentId = "66666666-6666-4666-8666-666666666666";
const clinicName = "Northside Demo Clinic";
const timeZone = "America/Chicago";
const referralPath = "/api/referrals/" + referralId;
const appointmentPath = "/api/appointments/" + appointmentId;
const clinicReferralPath = "/api/clinics/" + clinicId + "/referrals/" + referralId;
const availabilityPath = "/api/providers/" + providerId + "/availability";

// Monday in the clinic so Tuesday and Wednesday are valid booking days.
const now = new Date("2026-11-09T16:00:00Z");
const originalDate = "2026-11-10";
const rescheduleDate = "2026-11-11";
const originalSlot = { startUtc: originalDate + "T15:00:00Z", endUtc: originalDate + "T15:30:00Z" };
const takenSlot = { startUtc: rescheduleDate + "T15:00:00Z", endUtc: rescheduleDate + "T15:30:00Z" };
const replacementSlot = { startUtc: rescheduleDate + "T16:00:00Z", endUtc: rescheduleDate + "T16:30:00Z" };
const conflictMessage = "This slot is no longer available. Choose another time.";

test("clinician review hands off to front-desk booking, rescheduling, and cancellation", async ({ browser, baseURL }) => {
  const clinician = await browser.newContext({ baseURL });
  const frontDesk = await browser.newContext({ baseURL });
  let status: ReferralDetails["status"] = "PendingReview";
  let appointment: AppointmentDetails | undefined;
  let taken = false;
  const writes: { method: string; path: string; body?: unknown }[] = [];
  const unexpected: string[] = [];

  async function mockClinicApi(context: BrowserContext, role: "Clinician" | "FrontDesk") {
    const account: ReferralAccount = {
      isAdministrator: false,
      canCreateReferrals: role === "Clinician",
      providerPublicId: role === "Clinician" ? providerId : null,
      patientPublicId: null,
      memberships: [{ clinicPublicId: clinicId, clinicName, role }],
    };

    await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      const path = url.pathname;
      const method = request.method();

      if (method === "GET" && path === "/api/me") return route.fulfill({ json: account });
      if (method === "GET" && path === "/api/appointments/needs-attention") {
        return route.fulfill({ json: [] });
      }
      if (method === "GET" && path === clinicReferralPath) {
        const confirmed = appointment?.status === "Confirmed" ? appointment : null;
        const referral: ReferralDetails = {
          publicId: referralId,
          patientPublicId: "11111111-1111-4111-8111-111111111111",
          patientFirstName: "Alex",
          patientLastName: "Demo",
          clinicalServicePublicId: "22222222-2222-4222-8222-222222222222",
          serviceName: "Physical exam",
          clinicPublicId: clinicId,
          clinicName,
          clinicTimeZoneId: timeZone,
          assignedProviderPublicId: providerId,
          assignedProviderName: "Beth Demo",
          referringProviderName: "Dr. Demo",
          reason: "Follow-up physical exam",
          missingInformationNote: null,
          createdAtUtc: "2026-11-09T15:00:00Z",
          status,
          stage: confirmed ? "Scheduled" : status === "MissingInformation" ? "PendingReview" : status,
          canReview: role === "Clinician",
          canReassign: false,
          canSchedule: role === "FrontDesk",
          confirmedAppointment: confirmed,
        };
        return route.fulfill({ json: referral });
      }
      if (role === "Clinician" && method === "PATCH" && path === referralPath) {
        const body = request.postDataJSON() as ReferralUpdate;
        writes.push({ method, path, body });
        if (status === "PendingReview" && body.status === "ReadyToSchedule") {
          status = body.status;
          return route.fulfill({ status: 204 });
        }
      }
      if (role === "FrontDesk") {
        if (method === "GET" && path === availabilityPath) {
          const date = url.searchParams.get("date");
          const slots = date === originalDate ? [originalSlot]
            : date === rescheduleDate ? (taken ? [replacementSlot] : [takenSlot, replacementSlot]) : [];
          return route.fulfill({ json: { date, slots, timeZoneId: timeZone } });
        }
        if (method === "POST" && path === referralPath + "/appointments" && status === "ReadyToSchedule" && !appointment) {
          writes.push({ method, path, body: request.postDataJSON() });
          appointment = {
            publicId: appointmentId,
            ...originalSlot,
            clinicPublicId: clinicId,
            clinicName,
            timeZoneId: timeZone,
            patientName: "Alex Demo",
            providerPublicId: providerId,
            providerName: "Beth Demo",
            serviceName: "Physical exam",
            referralPublicId: referralId,
            status: "Confirmed",
            canCancel: true,
            canReschedule: true,
            overlapsProviderTimeOff: false,
          };
          return route.fulfill({ status: 201, json: { publicId: appointmentId } });
        }
        if (method === "GET" && path === appointmentPath && appointment) {
          return route.fulfill({ json: appointment });
        }
        if (method === "PUT" && path === appointmentPath + "/reschedule" && appointment?.status === "Confirmed") {
          const body = request.postDataJSON() as { expectedStartUtc: string; startUtc: string };
          writes.push({ method, path, body });
          if (body.startUtc === takenSlot.startUtc) {
            taken = true;
            return route.fulfill({ status: 409, json: { message: conflictMessage } });
          }
          if (body.expectedStartUtc === appointment.startUtc && body.startUtc === replacementSlot.startUtc) {
            appointment = { ...appointment, ...replacementSlot };
            return route.fulfill({ status: 204 });
          }
        }
        if (method === "POST" && path === appointmentPath + "/cancel" && appointment?.status === "Confirmed") {
          writes.push({ method, path });
          appointment = { ...appointment, status: "Cancelled", canCancel: false, canReschedule: false };
          return route.fulfill({ status: 204 });
        }
      }

      unexpected.push(method + " " + path);
      await route.fulfill({ status: 500, json: { message: "Unexpected API request in browser test." } });
    });
  }

  try {
    await mockClinicApi(clinician, "Clinician");
    const reviewPage = await clinician.newPage();
    await reviewPage.clock.setFixedTime(now);
    const referralUrl = "/referrals/" + referralId + "?clinic=" + clinicId;
    await reviewPage.goto(referralUrl);
    await reviewPage.getByRole("button", { name: "Edit referral" }).click();
    await reviewPage.getByRole("combobox", { name: "Status", exact: true }).selectOption("ReadyToSchedule");
    await reviewPage.getByRole("button", { name: "Update referral" }).click();
    await expect(reviewPage.getByText("Status: Ready to schedule", { exact: true })).toBeVisible();
    await expect(reviewPage.getByRole("button", { name: "Book appointment" })).toHaveCount(0);

    await mockClinicApi(frontDesk, "FrontDesk");
    const bookingPage = await frontDesk.newPage();
    await bookingPage.clock.setFixedTime(now);
    await bookingPage.goto(referralUrl);
    const book = bookingPage.getByRole("region", { name: "Book an appointment" });
    await expect(bookingPage.getByRole("button", { name: "Edit referral" })).toHaveCount(0);
    await expect(book.getByLabel("Appointment date")).toHaveAttribute("min", originalDate);
    await book.getByLabel("Appointment date").fill(originalDate);
    await book.getByLabel("Available time").selectOption(originalSlot.startUtc);
    await book.getByRole("button", { name: "Book appointment" }).click();
    await expect(bookingPage.getByRole("region", { name: "Scheduled appointment" })).toBeVisible();
    await bookingPage.getByRole("link", { name: "View appointment" }).click();
    await expect(bookingPage.getByRole("heading", { name: "Appointment details" })).toBeVisible();
    const scheduledTime = bookingPage.locator("time[datetime]");
    await expect(scheduledTime).toHaveAttribute("datetime", originalSlot.startUtc);

    await bookingPage.getByRole("button", { name: "Reschedule", exact: true }).click();
    await bookingPage.getByLabel("New appointment date").fill(rescheduleDate);
    await bookingPage.getByLabel("New appointment time").selectOption(takenSlot.startUtc);
    await bookingPage.getByRole("button", { name: "Save new time" }).click();
    await expect(bookingPage.getByRole("alert")).toHaveText(conflictMessage);
    await expect(scheduledTime).toHaveAttribute("datetime", originalSlot.startUtc);
    await expect(bookingPage.getByLabel("New appointment time")).toHaveValue("");
    await expect(bookingPage.getByLabel("New appointment time").locator('option[value="' + takenSlot.startUtc + '"]')).toHaveCount(0);

    await bookingPage.getByLabel("New appointment time").selectOption(replacementSlot.startUtc);
    await bookingPage.getByRole("button", { name: "Save new time" }).click();
    await expect(bookingPage.getByText("Appointment rescheduled.")).toBeVisible();
    await expect(scheduledTime).toHaveAttribute("datetime", replacementSlot.startUtc);

    bookingPage.once("dialog", (dialog) => dialog.accept());
    await bookingPage.getByRole("button", { name: "Cancel appointment" }).click();
    await expect(bookingPage.getByText("Cancelled", { exact: true })).toBeVisible();
    await bookingPage.getByRole("link", { name: "View referral" }).click();
    await expect(bookingPage.getByText("Status: Ready to schedule", { exact: true })).toBeVisible();
    await expect(bookingPage.getByRole("heading", { name: "Book an appointment" })).toBeVisible();

    expect(unexpected).toEqual([]);
    expect(writes).toEqual([
      { method: "PATCH", path: referralPath, body: {
        expectedProviderPublicId: providerId,
        expectedStatus: "PendingReview",
        expectedMissingInformationNote: null,
        status: "ReadyToSchedule",
      } },
      { method: "POST", path: referralPath + "/appointments", body: {
        providerPublicId: providerId, startUtc: originalSlot.startUtc,
      } },
      { method: "PUT", path: appointmentPath + "/reschedule", body: {
        expectedStartUtc: originalSlot.startUtc, startUtc: takenSlot.startUtc,
      } },
      { method: "PUT", path: appointmentPath + "/reschedule", body: {
        expectedStartUtc: originalSlot.startUtc, startUtc: replacementSlot.startUtc,
      } },
      { method: "POST", path: appointmentPath + "/cancel" },
    ]);
  } finally {
    await Promise.all([clinician.close(), frontDesk.close()]);
  }
});
