import { expect, test } from "@playwright/test";
import type {
  ReferralAccount,
  ReferralDetails,
  ReferralReceivingOptions,
  ReferralSummary,
} from "../src/api/referrals.ts";

const account: ReferralAccount = {
  isAdministrator: true,
  canCreateReferrals: true,
  providerPublicId: null,
  patientPublicId: null,
  memberships: [],
};

const receivingOptions: ReferralReceivingOptions = {
  referringProviderName: "Administrator Demo",
  destinations: [{
    clinicPublicId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    clinicName: "Northside Demo Clinic",
    clinicalServicePublicId: "22222222-2222-4222-8222-222222222222",
    serviceName: "Physical exam",
    providerPublicId: "33333333-3333-4333-8333-333333333333",
    providerName: "Beth Demo",
  }],
};

const alex = {
  publicId: "11111111-1111-4111-8111-111111111111",
  firstName: "Alex",
  lastName: "Demo",
  dateOfBirth: "1990-06-01",
};

const pendingReferral = {
  publicId: "11111111-2222-4333-8444-555555555555",
  patientFirstName: "Alex",
  patientLastName: "Demo",
  serviceName: "Physical exam",
  clinicName: "Northside Demo Clinic",
  status: "PendingReview",
  stage: "PendingReview",
  createdAtUtc: "2026-11-12T14:00:00Z",
} satisfies ReferralSummary;

const referrals: ReferralSummary[] = [
  pendingReferral,
  { ...pendingReferral, publicId: "22222222-2222-4333-8444-555555555555", patientFirstName: "Jamie" },
  { ...pendingReferral, publicId: "33333333-2222-4333-8444-555555555555", serviceName: "Therapy", status: "Closed", stage: "Closed" },
];

test("canceling a referral draft restores the queue filters without submitting", async ({ page }) => {
  const responses = new Map<string, unknown>([
    ["/api/me", account],
    ["/api/referrals", referrals],
    ["/api/referrals/receiving-options", receivingOptions],
    ["/api/patients", [alex]],
    ["/api/appointments/needs-attention", []],
  ]);
  const unexpectedRequests: string[] = [];

  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === "GET" && responses.has(path)) {
      await route.fulfill({ json: responses.get(path) });
      return;
    }
    unexpectedRequests.push(`${request.method()} ${path}`);
    await route.fulfill({ status: 500, json: { message: "Unexpected API request in browser test." } });
  });

  await page.goto("/referrals");
  const queue = page.getByRole("table", { name: "Referral queue" });
  await expect(queue.getByRole("row")).toHaveCount(4);

  await page.getByRole("button", { name: "Filters", exact: true }).click();
  const filters = page.getByRole("dialog", { name: "Filter referrals" });
  await filters.getByLabel("Status").selectOption("PendingReview");
  await filters.getByLabel("Patient", { exact: true }).fill("Alex");
  await filters.getByRole("button", { name: "Apply filters" }).click();
  await expect(queue.getByRole("row")).toHaveCount(2);
  await expect(queue.getByRole("cell", { name: "Physical exam Northside Demo Clinic", exact: true })).toBeVisible();

  await page.getByRole("link", { name: "New referral" }).click();
  await expect(page.getByRole("heading", { name: "New referral" })).toBeVisible();
  await page.getByLabel("Clinical service").selectOption({ label: "Physical exam" });
  await page.getByLabel("Receiving clinic and provider").selectOption({ label: "Northside Demo Clinic — Beth Demo" });
  await page.getByRole("combobox", { name: "Patient", exact: true }).selectOption({ label: "Demo, Alex" });
  await page.getByLabel("Reason").fill("Draft reason for the browser test.");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();

  await expect(page).toHaveURL(/\/referrals\?stage=PendingReview$/);
  await expect(queue.getByRole("row")).toHaveCount(2);
  await expect(queue.getByText("Alex Demo", { exact: true })).toBeVisible();
  await expect(queue.getByRole("cell", { name: "Physical exam Northside Demo Clinic", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  await expect(filters.getByLabel("Status")).toHaveValue("PendingReview");
  await expect(filters.getByLabel("Patient", { exact: true })).toHaveValue("Alex");
  expect(unexpectedRequests).toEqual([]);
});

test("submitting a referral sends one request and opens the new record", async ({ page }) => {
  const reason = "Follow-up physical exam for the browser test.";
  const createdReferral: ReferralDetails = {
    ...pendingReferral,
    patientPublicId: alex.publicId,
    clinicalServicePublicId: receivingOptions.destinations[0].clinicalServicePublicId,
    clinicPublicId: receivingOptions.destinations[0].clinicPublicId,
    clinicTimeZoneId: "America/Chicago",
    assignedProviderPublicId: receivingOptions.destinations[0].providerPublicId,
    assignedProviderName: receivingOptions.destinations[0].providerName,
    canReview: true,
    canReassign: false,
    canSchedule: false,
    confirmedAppointment: null,
    referringProviderName: receivingOptions.referringProviderName,
    reason,
    missingInformationNote: null,
  };
  const responses = new Map<string, unknown>([
    ["/api/me", account],
    ["/api/referrals/receiving-options", receivingOptions],
    ["/api/patients", [alex]],
    ["/api/appointments/needs-attention", []],
  ]);
  const submissions: unknown[] = [];

  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;

    if (request.method() === "POST" && path === "/api/referrals") {
      submissions.push(request.postDataJSON());
      await route.fulfill({ status: 201, json: { publicId: createdReferral.publicId } });
      return;
    }
    if (request.method() === "GET" && path === `/api/referrals/${createdReferral.publicId}` && submissions.length > 0) {
      await route.fulfill({ json: createdReferral });
      return;
    }
    if (request.method() === "GET" && responses.has(path)) {
      await route.fulfill({ json: responses.get(path) });
      return;
    }
    await route.fulfill({ status: 500, json: { message: "Unexpected API request in browser test." } });
  });

  await page.goto("/referrals/new");
  await page.getByLabel("Clinical service").selectOption({ label: "Physical exam" });
  await page.getByLabel("Receiving clinic and provider").selectOption({ label: "Northside Demo Clinic — Beth Demo" });
  await page.getByRole("combobox", { name: "Patient", exact: true }).selectOption({ label: "Demo, Alex" });
  await page.getByLabel("Reason").fill(reason);
  await page.getByRole("button", { name: "Create referral", exact: true }).click();

  await expect(page).toHaveURL(new RegExp(`/referrals/${createdReferral.publicId}$`));
  await expect(page.getByRole("heading", { name: "Referral details" })).toBeVisible();
  await expect(page.getByText(reason, { exact: true })).toBeVisible();
  expect(submissions).toEqual([{
    patientPublicId: alex.publicId,
    clinicalServicePublicId: createdReferral.clinicalServicePublicId,
    assignedProviderPublicId: createdReferral.assignedProviderPublicId,
    reason,
  }]);
});
