import { expect, test, type BrowserContext, type Route } from "@playwright/test";
import type { AppointmentDetails, AppointmentSummary, PatientAppointment } from "../src/api/appointments";
import type { ProviderCalendar } from "../src/api/providerSchedule";
import type { PatientReferralSummary, ReferralAccount } from "../src/api/referrals";

const clinicId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const providerId = "33333333-3333-4333-8333-333333333333";
const patientId = "11111111-1111-4111-8111-111111111111";
const referralId = "22222222-2222-4222-8222-222222222222";
const appointmentId = "44444444-4444-4444-8444-444444444444";
const dayOffId = "55555555-5555-4555-8555-555555555555";
const clinicName = "Northside Demo Clinic";
const timeZoneId = "America/Chicago";
const today = "2026-11-09";
const bookedDate = "2026-11-10";

const patientAccount: ReferralAccount = {
  isAdministrator: false,
  canCreateReferrals: false,
  providerPublicId: null,
  patientPublicId: patientId,
  memberships: [],
};

const provider: ReferralAccount = {
  ...patientAccount,
  canCreateReferrals: true,
  providerPublicId: providerId,
  patientPublicId: null,
  memberships: [{ clinicPublicId: clinicId, clinicName, role: "Clinician" }],
};

const patientReferral: PatientReferralSummary = {
  publicId: referralId,
  serviceName: "Therapy",
  clinicName,
  status: "ReadyToSchedule",
  stage: "Scheduled",
  createdAtUtc: "2026-11-09T15:00:00Z",
};

const patientAppointment: PatientAppointment = {
  publicId: appointmentId,
  serviceName: patientReferral.serviceName,
  clinicName,
  timeZoneId,
  startUtc: bookedDate + "T15:00:00Z",
  endUtc: bookedDate + "T15:30:00Z",
  providerName: "Beth Demo",
  status: "Confirmed",
};

const conflict: AppointmentSummary = {
  publicId: appointmentId,
  startUtc: patientAppointment.startUtc,
  endUtc: patientAppointment.endUtc,
  clinicPublicId: clinicId,
  clinicName,
  timeZoneId,
  patientName: "Alex Demo",
  providerName: patientAppointment.providerName,
  serviceName: patientAppointment.serviceName,
  referralPublicId: referralId,
};

async function mockApi(
  context: BrowserContext,
  account: ReferralAccount,
  respond: (route: Route, method: string, path: string) => Promise<void> | undefined,
) {
  const unexpected: string[] = [];
  await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    if (method === "GET" && path === "/api/me") {
      await route.fulfill({ json: account });
      return;
    }

    const response = respond(route, method, path);
    if (response) {
      await response;
      return;
    }

    if (method === "GET" && path === "/api/appointments/needs-attention/count" && account.providerPublicId) {
      return route.fulfill({ json: { count: 0 } });
    }

    unexpected.push(method + " " + path);
    await route.fulfill({ status: 500, json: { message: "Unexpected API request in browser test." } });
  });
  return unexpected;
}

test("clinician completes a started appointment from their calendar", async ({ context, page }) => {
  const appointmentPath = "/api/appointments/" + appointmentId;
  let completionRequests = 0;
  let detailRequests = 0;
  const appointment: AppointmentDetails = {
    ...conflict,
    providerPublicId: providerId,
    status: "Confirmed",
    canCancel: false,
    canComplete: true,
    canReschedule: false,
    overlapsProviderTimeOff: false,
  };
  const calendar: ProviderCalendar = {
    today: bookedDate,
    timeZoneId,
    days: [{
      date: bookedDate,
      openSlots: 0,
      availableSlots: [],
      startedSlots: [],
      dayOff: false,
      dayOffPublicId: null,
      canMarkUnavailable: true,
      timeOff: [],
      appointments: [{ ...conflict, status: "Confirmed", duringTimeOff: false }],
    }],
  };
  const unexpected = await mockApi(context, provider, (route, method, path) => {
    if (method === "GET" && path === "/api/me/calendar") return route.fulfill({ json: calendar });
    if (method === "GET" && path === appointmentPath) {
      detailRequests += 1;
      return route.fulfill({ json: appointment });
    }
    if (method === "POST" && path === appointmentPath + "/complete") {
      completionRequests += 1;
      if (completionRequests === 1) {
        return route.fulfill({ status: 409, json: { code: "appointment_changed", message: "The appointment changed. Refresh and try again." } });
      }
      appointment.status = "Completed";
      appointment.canComplete = false;
      calendar.days[0].appointments = [];
      return route.fulfill({ json: { publicId: appointmentId, status: "Completed" } });
    }
  });

  await page.clock.setFixedTime(new Date(conflict.endUtc));
  await page.goto("/my-schedule");
  await page.getByRole("link", { name: /^View appointment:/ }).click();
  await expect(page.getByRole("heading", { name: "Appointment details" })).toBeVisible();

  const complete = page.getByRole("button", { name: "Complete appointment" });
  page.once("dialog", (dialog) => dialog.dismiss());
  await complete.click();
  expect(completionRequests).toBe(0);

  const readsBeforeConflict = detailRequests;
  page.once("dialog", (dialog) => dialog.accept());
  await complete.click();
  await expect(page.getByRole("alert")).toHaveText("The appointment changed. Refresh and try again.");
  await expect.poll(() => detailRequests).toBeGreaterThan(readsBeforeConflict);
  await expect(page.getByText("Scheduled", { exact: true })).toBeVisible();
  await expect(complete).toBeEnabled();

  page.once("dialog", (dialog) => dialog.accept());
  await complete.click();
  await expect(page.getByRole("status")).toHaveText("Appointment completed.");
  await expect(page.getByText("Completed", { exact: true })).toBeVisible();
  await expect(complete).toHaveCount(0);
  await page.getByRole("link", { name: "Back to my schedule" }).click();
  await expect(page.getByText("No appointments booked.")).toBeVisible();
  expect(completionRequests).toBe(2);
  expect(unexpected).toEqual([]);
});

test("completion conflict refreshes a cancelled appointment and preserves the error", async ({ context, page }) => {
  const appointmentPath = "/api/appointments/" + appointmentId;
  const message = "Only a confirmed appointment can be cancelled or completed.";
  const appointment: AppointmentDetails = {
    ...conflict,
    providerPublicId: providerId,
    status: "Confirmed",
    canCancel: false,
    canComplete: true,
    canReschedule: false,
    overlapsProviderTimeOff: false,
  };
  let completionRequests = 0;
  const unexpected = await mockApi(context, provider, (route, method, path) => {
    if (method === "GET" && path === appointmentPath) return route.fulfill({ json: appointment });
    if (method === "POST" && path === appointmentPath + "/complete") {
      completionRequests += 1;
      appointment.status = "Cancelled";
      appointment.canComplete = false;
      return route.fulfill({ status: 409, json: { code: "appointment_not_confirmed", message } });
    }
  });

  await page.clock.setFixedTime(new Date(conflict.endUtc));
  await page.goto("/appointments/" + appointmentId);
  const complete = page.getByRole("button", { name: "Complete appointment" });
  await expect(complete).toBeEnabled();
  page.once("dialog", (dialog) => dialog.accept());
  await complete.click();
  await expect(page.getByText("Cancelled", { exact: true })).toBeVisible();
  await expect(complete).toHaveCount(0);
  await expect(page.getByRole("alert")).toHaveText(message);
  expect(completionRequests).toBe(1);
  expect(unexpected).toEqual([]);
});

test("completion eligibility refreshes automatically after the appointment starts", async ({ context, page }) => {
  const appointmentPath = "/api/appointments/" + appointmentId;
  const appointment: AppointmentDetails = {
    ...conflict,
    providerPublicId: providerId,
    status: "Confirmed",
    canCancel: false,
    canComplete: false,
    canReschedule: false,
    overlapsProviderTimeOff: false,
  };
  let detailRequests = 0;
  const unexpected = await mockApi(context, provider, (route, method, path) => {
    if (method === "GET" && path === appointmentPath) {
      detailRequests += 1;
      return route.fulfill({ json: appointment });
    }
  });

  await page.clock.install({ time: new Date(Date.parse(conflict.startUtc) - 60_000) });
  await page.goto("/appointments/" + appointmentId);
  await expect(page.getByText("Scheduled", { exact: true })).toBeVisible();
  const complete = page.getByRole("button", { name: "Complete appointment" });
  await expect(page.getByRole("button", { name: "Refresh", exact: true })).toHaveCount(0);
  await expect(complete).toHaveCount(0);

  const readsBeforeStart = detailRequests;
  const startCheck = page.waitForResponse(response => new URL(response.url()).pathname === appointmentPath);
  await page.clock.runFor(60_300);
  await (await startCheck).finished();
  await expect.poll(() => detailRequests).toBeGreaterThan(readsBeforeStart);
  // Passing the start time must not override a negative server capability.
  await expect(complete).toHaveCount(0);

  appointment.canComplete = true;
  const eligibilityCheck = page.waitForResponse(response => new URL(response.url()).pathname === appointmentPath);
  await page.clock.runFor(15_100);
  await (await eligibilityCheck).finished();
  await expect(complete).toBeEnabled();
  expect(unexpected).toEqual([]);
});

test("linked clinician can cancel an upcoming appointment after confirming", async ({ context, page }) => {
  const appointmentPath = "/api/appointments/" + appointmentId;
  const appointment: AppointmentDetails = {
    ...conflict,
    providerPublicId: providerId,
    status: "Confirmed",
    canCancel: true,
    canComplete: false,
    canReschedule: false,
    overlapsProviderTimeOff: false,
  };
  let cancellationRequests = 0;
  const unexpected = await mockApi(context, provider, (route, method, path) => {
    if (method === "GET" && path === appointmentPath) return route.fulfill({ json: appointment });
    if (method === "POST" && path === appointmentPath + "/cancel") {
      cancellationRequests += 1;
      appointment.status = "Cancelled";
      appointment.canCancel = false;
      return route.fulfill({ json: { publicId: appointmentId, status: "Cancelled" } });
    }
  });

  await page.clock.setFixedTime(new Date(today + "T16:00:00Z"));
  await page.goto("/appointments/" + appointmentId);
  const cancel = page.getByRole("button", { name: "Cancel appointment" });
  await expect(cancel).toBeVisible();
  await expect(page.getByRole("button", { name: "Reschedule" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Complete appointment" })).toHaveCount(0);
  page.once("dialog", (dialog) => dialog.dismiss());
  await cancel.click();
  expect(cancellationRequests).toBe(0);
  page.once("dialog", (dialog) => dialog.accept());
  await cancel.click();
  await expect(page.getByRole("status")).toHaveText("Appointment canceled.");
  await expect(page.getByText("Cancelled", { exact: true })).toBeVisible();
  await expect(cancel).toHaveCount(0);
  expect(cancellationRequests).toBe(1);
  expect(unexpected).toEqual([]);
});

test("provider time off preserves the booking and appears in the front-desk conflict queue", async ({ context, page, browser, baseURL }) => {
  let dayOff = false;
  let timeOffRequests = 0;
  const timeOffPath = "/api/me/calendar/" + bookedDate + "/time-off";
  function respondToConflictQueue(route: Route, method: string, path: string) {
    if (method === "GET" && path === "/api/appointments/needs-attention/count") {
      return route.fulfill({ json: { count: dayOff ? 1 : 0 } });
    }
    if (method === "POST" && path === "/api/appointments/needs-attention/search") {
      return route.fulfill({ json: { items: dayOff ? [conflict] : [], page: 1, pageSize: 14,
        totalCount: dayOff ? 1 : 0, hasMore: false } });
    }
  }
  const providerRequests = await mockApi(context, provider, (route, method, path) => {
    if (method === "GET" && path === "/api/me/calendar") {
      const calendar: ProviderCalendar = {
        today,
        timeZoneId,
        days: [{
          date: bookedDate,
          openSlots: 0,
          availableSlots: [],
          startedSlots: [],
          dayOff,
          dayOffPublicId: dayOff ? dayOffId : null,
          canMarkUnavailable: !dayOff,
          timeOff: [{
            startUtc: bookedDate + "T17:00:00Z",
            endUtc: bookedDate + "T17:30:00Z",
          }],
          appointments: [{
            publicId: appointmentId,
            startUtc: conflict.startUtc,
            endUtc: conflict.endUtc,
            serviceName: conflict.serviceName,
            patientName: conflict.patientName,
            status: "Confirmed",
            duringTimeOff: dayOff,
          }],
        }],
      };
      return route.fulfill({ json: calendar });
    }
    if (method === "POST" && path === timeOffPath) {
      timeOffRequests++;
      dayOff = true;
      return route.fulfill({ status: 201, json: { publicId: dayOffId } });
    }
    if (method === "GET" && path === "/api/appointments/" + appointmentId) {
      return route.fulfill({ json: {
        ...conflict, providerPublicId: providerId, status: "Confirmed", canCancel: true,
        canReschedule: false, canComplete: false, overlapsProviderTimeOff: dayOff,
      } });
    }
    return respondToConflictQueue(route, method, path);
  });

  const frontDesk = await browser.newContext({ baseURL });
  try {
    const frontDeskRequests = await mockApi(frontDesk, {
      ...patientAccount,
      patientPublicId: null,
      memberships: [{ clinicPublicId: clinicId, clinicName, role: "FrontDesk" }],
    }, respondToConflictQueue);

    const frontDeskPage = await frontDesk.newPage();
    await frontDeskPage.clock.install();
    await frontDeskPage.goto("/appointments/availability-conflicts");
    await expect(frontDeskPage.getByText("No scheduled appointments overlap provider time off.")).toBeVisible();

    await page.clock.setFixedTime(new Date(today + "T16:00:00Z"));
    await page.goto("/my-schedule");
    await expect(page.getByText("Alex Demo · Therapy")).toBeVisible();
    await expect(page.getByRole("list", { name: "Time off" })).toContainText(/11:00.*11:30/);
    const markUnavailable = page.getByRole("button", { name: "Mark day unavailable" });

    page.once("dialog", (dialog) => dialog.dismiss());
    await markUnavailable.click();
    await expect(markUnavailable).toBeEnabled();

    page.once("dialog", (dialog) => dialog.accept());
    const timeOffRequest = page.waitForRequest((request) =>
      request.method() === "POST" && new URL(request.url()).pathname === timeOffPath,
    );
    await markUnavailable.click();
    await timeOffRequest;
    await expect(page.getByText("Day marked unavailable.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Remove day off" })).toBeVisible();
    await expect(page.getByText("Alex Demo · Therapy")).toBeVisible();
    await expect(page.getByText("Scheduled", { exact: true })).toBeVisible();
    await expect(page.getByText("Time-off conflict")).toBeVisible();
    await expect(page.getByText("Time off: All day")).toBeVisible();

    await expect(frontDeskPage.getByRole("button", { name: "Refresh", exact: true })).toHaveCount(0);
    await frontDeskPage.clock.runFor(30_000);
    const card = frontDeskPage.getByRole("listitem").filter({ hasText: "Alex Demo" });
    await expect(card.getByText("Overlaps provider time off")).toBeVisible();
    await expect(card.getByRole("link", { name: /^View appointment:/ }))
      .toHaveAttribute("href", "/appointments/" + appointmentId);
    await expect(frontDeskPage.getByRole("navigation", { name: "Main navigation" })
      .getByRole("link", { name: /1 appointments with availability conflicts/ })).toBeVisible();

    const clinicianNav = page.getByRole("navigation", { name: "Main navigation" });
    await expect(clinicianNav.getByRole("link", { name: "Upcoming appointments" })).toHaveCount(0);
    await clinicianNav.getByRole("link", { name: /1 appointments with availability conflicts/ }).click();
    await page.getByRole("link", { name: /^View appointment: Alex Demo/ }).click();
    await expect(page.getByText("Contact front desk to coordinate rescheduling with the patient.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Reschedule", exact: true })).toHaveCount(0);
    await page.getByRole("link", { name: "Back to availability conflicts" }).click();
    await expect(page.getByRole("heading", { name: "Availability conflicts" })).toBeVisible();

    expect(timeOffRequests).toBe(1);
    expect(providerRequests).toEqual([]);
    expect(frontDeskRequests).toEqual([]);
  } finally {
    await frontDesk.close();
  }
});

test("clinician can review full months and past appointments from the schedule", async ({ context, page }) => {
  const historyDate = "2026-10-15";
  const pastAppointment: AppointmentDetails = {
    ...conflict,
    providerPublicId: providerId,
    startUtc: "2026-10-16T02:00:00Z",
    endUtc: "2026-10-16T02:30:00Z",
    status: "Completed",
    canCancel: false,
    canComplete: false,
    canReschedule: false,
    overlapsProviderTimeOff: false,
  };
  const daySlots = ["T14:00:00Z", "T19:00:00Z"].flatMap((periodStart) =>
    Array.from({ length: 8 }, (_, index) => {
      const start = Date.parse(today + periodStart) + index * 30 * 60_000;
      return { startUtc: new Date(start).toISOString(), endUtc: new Date(start + 30 * 60_000).toISOString() };
    }),
  );
  const bookedSlot = daySlots[8];
  const availableSlots = daySlots.filter(slot => slot !== bookedSlot);
  const afternoonAppointment: AppointmentDetails = {
    ...pastAppointment, ...bookedSlot, publicId: "66666666-6666-4666-8666-666666666666", status: "Confirmed",
  };
  const firstDay = Date.parse("2026-10-01");
  const dayCount = (Date.parse("2027-03-01") - firstDay) / 86_400_000;
  const calendar: ProviderCalendar = {
    today,
    timeZoneId,
    days: Array.from({ length: dayCount }, (_, offset) => {
      const date = new Date(firstDay + offset * 86_400_000).toISOString().slice(0, 10);
      return {
        date,
        openSlots: date === today ? availableSlots.length : 0,
        availableSlots: date === today ? availableSlots : [],
        startedSlots: [],
        dayOff: false,
        dayOffPublicId: null,
        canMarkUnavailable: date >= today && date < "2027-02-07",
        timeOff: [],
        appointments: date === historyDate ? [{ ...pastAppointment, duringTimeOff: false }]
          : date === today ? [{ ...afternoonAppointment, duringTimeOff: false }] : [],
      };
    }),
  };
  const unexpected = await mockApi(context, provider, (route, method, path) => {
    if (method === "GET" && path === "/api/me/calendar") return route.fulfill({ json: calendar });
    if (method === "GET" && path === "/api/appointments/" + appointmentId) return route.fulfill({ json: pastAppointment });
    if (method === "GET" && path === "/api/appointments/" + afternoonAppointment.publicId) {
      return route.fulfill({ json: afternoonAppointment });
    }
  });

  await page.setViewportSize({ width: 1600, height: 900 });
  await page.clock.setFixedTime(new Date(today + "T13:00:00Z"));
  await page.goto("/my-schedule?date=2026-11-08");

  const calendarGrid = page.getByRole("group", { name: "Calendar dates" });
  const dates = calendarGrid.getByRole("button");
  await expect(dates).toHaveCount(30);
  await expect(calendarGrid.locator(".provider-calendar-cell")).toHaveCount(35);
  await expect(page.getByRole("button", { name: /November 9, 2026/ })).toHaveAttribute("aria-current", "date");
  await expect(page.getByRole("button", { name: /November 9, 2026/ })).toHaveAttribute("aria-pressed", "true");
  await expect(dates.filter({ hasText: "Weekend" })).toHaveCount(9);
  await expect(calendarGrid.getByRole("button", { name: /Saturday, November 7, 2026/ })).toBeDisabled();
  await expect(calendarGrid.getByRole("button", { name: /Sunday, November 8, 2026/ })).toBeDisabled();
  const cellSizes = await dates.evaluateAll((cells) => cells.map((cell) => {
    const { width, height } = cell.getBoundingClientRect();
    return [Math.round(width), Math.round(height)].join(" × ");
  }));
  expect(new Set(cellSizes).size).toBe(1);

  const agenda = page.getByRole("region", { name: /November 9, 2026/ });
  const month = page.getByRole("region", { name: "November 2026" });
  const schedule = page.getByRole("region", { name: "My schedule", exact: true });
  expect((await schedule.boundingBox())!.width).toBeGreaterThan(1152);
  expect((await agenda.boundingBox())!.x).toBeLessThan((await month.boundingBox())!.x);
  const heading = await agenda.getByRole("heading", { name: /November 9, 2026/ }).boundingBox();
  const openTimes = await agenda.getByText("15 open times", { exact: true }).boundingBox();
  const appointments = await agenda.getByText("1 appointment", { exact: true }).boundingBox();
  expect(openTimes!.x).toBeGreaterThanOrEqual(heading!.x + heading!.width);
  expect(Math.abs(heading!.y - openTimes!.y)).toBeLessThanOrEqual(4);
  expect(appointments!.y).toBe(openTimes!.y);
  await expect(month.getByRole("button", { name: "Mark day unavailable" })).toHaveAttribute("data-variant", "danger");
  const times = agenda.getByRole("region", { name: "Appointment times" });
  const morning = times.getByRole("region", { name: "Morning", exact: true });
  const afternoon = times.getByRole("region", { name: "Afternoon", exact: true });
  await expect(morning.getByRole("listitem")).toHaveCount(8);
  await expect(afternoon.getByRole("listitem")).toHaveCount(8);
  expect((await morning.boundingBox())!.x).toBeLessThan((await afternoon.boundingBox())!.x);
  expect(await times.evaluate((list) => getComputedStyle(list).overflowY)).toBe("visible");
  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(901);
  const rowHeights = await times.getByRole("listitem").evaluateAll(
    rows => rows.map(row => row.getBoundingClientRect().height),
  );
  expect(new Set(rowHeights).size).toBe(1);
  await expect(morning.getByRole("link")).toHaveCount(0);
  const bookedRow = afternoon.getByRole("listitem").first();
  const bookedLink = bookedRow.getByRole("link", { name: /^View appointment:.*Alex Demo/ });
  const rowBounds = (await bookedRow.boundingBox())!;
  const linkBounds = (await bookedLink.boundingBox())!;
  expect(rowBounds.width - linkBounds.width).toBeLessThanOrEqual(2);
  expect(rowBounds.height - linkBounds.height).toBeLessThanOrEqual(2);

  await page.setViewportSize({ width: 390, height: 844 });
  expect((await month.boundingBox())!.y).toBeLessThan((await agenda.boundingBox())!.y);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await expect(morning).toBeVisible();
  await expect(afternoon).toBeHidden();
  const afternoonToggle = agenda.getByRole("button", { name: "Afternoon", exact: true });
  await afternoonToggle.press("Enter");
  await expect(afternoonToggle).toHaveAttribute("aria-pressed", "true");
  await expect(morning).toBeHidden();
  await bookedLink.press("Enter");
  await expect(page.getByRole("definition").filter({ hasText: "Scheduled" })).toBeVisible();
  await page.goBack();
  await expect(afternoonToggle).toHaveAttribute("aria-pressed", "true");
  await page.reload();
  await expect(afternoonToggle).toHaveAttribute("aria-pressed", "true");
  await expect(afternoon).toBeVisible();

  const previous = page.getByRole("button", { name: "Previous month" });
  const next = page.getByRole("button", { name: "Next month" });
  await expect(previous).toBeEnabled();
  await previous.click();
  await expect(page.getByRole("heading", { name: "October 2026" })).toBeVisible();
  await expect(dates).toHaveCount(31);
  await expect(previous).toBeDisabled();
  await page.getByRole("button", { name: /October 15, 2026/ }).click();
  await expect(page.getByText("Completed", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Mark day unavailable" })).toHaveCount(0);
  await page.getByRole("link", { name: /^View appointment:/ }).click();
  await expect(page.getByRole("definition").filter({ hasText: "Completed" })).toBeVisible();
  await expect(page.getByRole("region", { name: "Appointment actions" })).toHaveCount(0);
  await page.getByRole("link", { name: "Back to my schedule" }).click();
  await expect(page.getByRole("heading", { name: /October 15, 2026/ })).toBeVisible();

  await page.goto("/appointments/" + appointmentId);
  await expect(page.getByRole("definition").filter({ hasText: "Completed" })).toBeVisible();
  await page.getByRole("link", { name: "Back to my schedule" }).click();
  await expect(page.getByRole("heading", { name: /October 15, 2026/ })).toBeVisible();

  for (const name of ["November 2026", "December 2026", "January 2027", "February 2027"]) {
    await next.click();
    await expect(page.getByRole("heading", { name })).toBeVisible();
  }
  await expect(dates).toHaveCount(28);
  await expect(next).toBeDisabled();
  expect(unexpected).toEqual([]);
});

test("patient workspace uses personal endpoints and hides staff controls", async ({ context, page }) => {
  const clinicAddress = "100 Demo Avenue";
  const otherVisit: PatientAppointment = {
    ...patientAppointment,
    publicId: "66666666-6666-4666-8666-666666666666",
    serviceName: "Physical exam",
    startUtc: "2026-11-12T15:00:00Z",
    endUtc: "2026-11-12T15:30:00Z",
  };
  const cancelledVisit: PatientAppointment = {
    ...patientAppointment,
    publicId: "77777777-7777-4777-8777-777777777777",
    status: "Cancelled",
  };
  const unexpected = await mockApi(context, patientAccount, (route, method, path) => {
    if (method === "GET" && path === "/api/me/referrals") return route.fulfill({ json: [patientReferral] });
    if (method === "GET" && path === "/api/me/appointments") {
      return route.fulfill({ json: [otherVisit, cancelledVisit, patientAppointment] });
    }
    if (method === "GET" && path === "/api/me/referrals/" + referralId) {
      return route.fulfill({ json: {
        ...patientReferral, clinicAddress, providerName: patientAppointment.providerName, appointmentPublicId: appointmentId,
      } });
    }
    if (method === "GET" && path === "/api/me/appointments/" + appointmentId) {
      return route.fulfill({ json: { ...patientAppointment, clinicAddress, referralPublicId: referralId } });
    }
  });

  await page.clock.install({ time: new Date(today + "T15:00:00Z") });
  await page.goto("/");
  await expect(page).toHaveURL(/\/my-referrals$/);
  const referralList = page.getByRole("list", { name: "My referrals" });
  await expect(referralList.getByRole("listitem")).toHaveCount(1);
  await expect(referralList.getByRole("heading", { name: "Therapy" })).toBeVisible();
  await expect(referralList.getByText("View referral", { exact: true })).toHaveCount(0);
  await referralList.getByRole("link", { name: /View referral: Therapy/ }).press("Enter");
  await expect(page.getByRole("heading", { name: "Referral details" })).toBeVisible();
  await expect(page.getByText(clinicAddress, { exact: false })).toBeVisible();
  const linkedVisit = page.getByRole("link", { name: "View appointment", exact: true });
  await expect(linkedVisit).toHaveAttribute("href", "/my-appointments/" + appointmentId);
  await linkedVisit.click();
  await expect(page.getByRole("heading", { name: "Appointment details" })).toBeVisible();
  await expect(page.getByText("Contact the clinic if you need to reschedule or cancel this appointment.")).toBeVisible();
  await expect(page.getByRole("link", { name: "View referral" })).toHaveAttribute("href", "/my-referrals/" + referralId);
  await expect(page.getByRole("button", { name: "Cancel appointment" })).toHaveCount(0);

  const navigation = page.getByRole("navigation", { name: "Main navigation" });
  await expect(navigation.getByRole("link", { name: "My referrals" })).toBeVisible();
  await expect(navigation.getByRole("link")).toHaveCount(2);
  await navigation.getByRole("link", { name: "My appointments" }).click();
  await expect(page).toHaveURL(/\/my-appointments$/);
  const appointmentList = page.getByRole("list", { name: "Upcoming" });
  await expect(appointmentList.getByRole("heading")).toHaveText(["Therapy", "Physical exam"]);
  const upcomingVisit = appointmentList.getByRole("listitem").first();
  await expect(upcomingVisit.getByText("Beth Demo · " + clinicName)).toBeVisible();
  await expect(upcomingVisit.getByText("Scheduled", { exact: true })).toBeVisible();
  await expect(upcomingVisit.getByRole("link", { name: "View appointment: Therapy" }))
    .toHaveAttribute("href", "/my-appointments/" + appointmentId);
  await expect(upcomingVisit.getByText("View appointment", { exact: true })).toHaveCount(0);
  await expect(appointmentList.getByRole("button")).toHaveCount(0);
  await expect(page.getByRole("list", { name: "History" }).getByText("Cancelled", { exact: true })).toBeVisible();
  await page.clock.setSystemTime(new Date(patientAppointment.startUtc));
  await page.clock.runFor(15_000);
  await expect(appointmentList.getByRole("heading")).toHaveText(["Physical exam"]);
  await expect(page.getByRole("list", { name: "History" }).getByText("Scheduled", { exact: true })).toBeVisible();

  expect(unexpected).toEqual([]);
});

test("an unknown URL lets a signed-in patient return to their workspace", async ({ context, page }) => {
  const unexpected = await mockApi(context, patientAccount, (route, method, path) => {
    if (method === "GET" && path === "/api/me/referrals") return route.fulfill({ json: [patientReferral] });
  });

  await page.goto("/unknown-workspace-page");
  await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
  const returnLink = page.getByRole("link", { name: "Return to workspace" });
  await expect(returnLink).toHaveAttribute("href", "/my-referrals");
  await returnLink.click();
  await expect(page).toHaveURL(/\/my-referrals$/);
  await expect(page.getByRole("heading", { name: "My referrals" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Therapy" })).toBeVisible();

  expect(unexpected).toEqual([]);
});
