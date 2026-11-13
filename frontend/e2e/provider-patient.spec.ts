import { expect, test, type BrowserContext, type Route } from "@playwright/test";
import type { AppointmentSummary, PatientAppointment } from "../src/api/appointments";
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

    unexpected.push(method + " " + path);
    await route.fulfill({ status: 500, json: { message: "Unexpected API request in browser test." } });
  });
  return unexpected;
}

test("provider time off preserves the booking and appears in the front-desk conflict queue", async ({ context, page, browser, baseURL }) => {
  let dayOff = false;
  let timeOffRequests = 0;
  const timeOffPath = "/api/me/calendar/" + bookedDate + "/time-off";
  const provider: ReferralAccount = {
    ...patientAccount,
    canCreateReferrals: true,
    providerPublicId: providerId,
    patientPublicId: null,
    memberships: [{ clinicPublicId: clinicId, clinicName, role: "Clinician" }],
  };
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
          appointments: [{
            publicId: appointmentId,
            startUtc: conflict.startUtc,
            endUtc: conflict.endUtc,
            serviceName: conflict.serviceName,
            patientName: conflict.patientName,
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
  });

  const frontDesk = await browser.newContext({ baseURL });
  try {
    const frontDeskRequests = await mockApi(frontDesk, {
      ...patientAccount,
      patientPublicId: null,
      memberships: [{ clinicPublicId: clinicId, clinicName, role: "FrontDesk" }],
    }, (route, method, path) => {
      if (method === "GET" && path === "/api/appointments/needs-attention") {
        return route.fulfill({ json: dayOff ? [conflict] : [] });
      }
    });

    const frontDeskPage = await frontDesk.newPage();
    await frontDeskPage.goto("/appointments/availability-conflicts");
    await expect(frontDeskPage.getByText("No confirmed appointments overlap provider time off.")).toBeVisible();

    await page.clock.setFixedTime(new Date(today + "T16:00:00Z"));
    await page.goto("/my-schedule");
    await expect(page.getByText("Therapy · Alex Demo")).toBeVisible();
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
    await expect(page.getByText("Therapy · Alex Demo")).toBeVisible();
    await expect(page.getByText("Booked during time off")).toBeVisible();

    await frontDeskPage.getByRole("button", { name: "Refresh", exact: true }).click();
    const card = frontDeskPage.getByRole("listitem").filter({ hasText: "Alex Demo" });
    await expect(card.getByText("Overlaps provider time off")).toBeVisible();
    await expect(card.getByRole("link", { name: "View appointment" }))
      .toHaveAttribute("href", "/appointments/" + appointmentId);
    await expect(frontDeskPage.getByRole("navigation", { name: "Main navigation" })
      .getByRole("link", { name: /1 appointments with availability conflicts/ })).toBeVisible();

    expect(timeOffRequests).toBe(1);
    expect(providerRequests).toEqual([]);
    expect(frontDeskRequests).toEqual([]);
  } finally {
    await frontDesk.close();
  }
});

test("patient workspace uses personal endpoints and hides staff controls", async ({ context, page }) => {
  const unexpected = await mockApi(context, patientAccount, (route, method, path) => {
    if (method === "GET" && path === "/api/me/referrals") return route.fulfill({ json: [patientReferral] });
    if (method === "GET" && path === "/api/me/appointments") return route.fulfill({ json: [patientAppointment] });
  });

  await page.goto("/");
  await expect(page).toHaveURL(/\/my-referrals$/);
  const referralList = page.getByRole("list", { name: "My referrals" });
  await expect(referralList.getByRole("listitem")).toHaveCount(1);
  await expect(referralList.getByRole("heading", { name: "Therapy" })).toBeVisible();

  const navigation = page.getByRole("navigation", { name: "Main navigation" });
  await expect(navigation.getByRole("link", { name: "My referrals" })).toBeVisible();
  await expect(navigation.getByRole("link")).toHaveCount(2);
  await navigation.getByRole("link", { name: "My appointments" }).click();
  await expect(page).toHaveURL(/\/my-appointments$/);
  const appointmentList = page.getByRole("list", { name: "My appointments" });
  await expect(appointmentList.getByRole("listitem")).toHaveCount(1);
  await expect(appointmentList.getByRole("heading", { name: "Therapy" })).toBeVisible();
  await expect(appointmentList.getByText("Beth Demo · " + clinicName)).toBeVisible();
  await expect(appointmentList.getByText("Scheduled", { exact: true })).toBeVisible();
  await expect(appointmentList.getByRole("button")).toHaveCount(0);
  await expect(appointmentList.getByRole("link")).toHaveCount(0);

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
