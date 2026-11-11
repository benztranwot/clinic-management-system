import { ClinicRequestError } from "./clinics";
import { PatientRequestError } from "./patients";
import { ProviderRequestError, type Provider } from "./providers";
import { ServiceRequestError } from "./services";

export const referralStatusLabels = {
  PendingReview: "Pending review",
  MissingInformation: "Missing information",
  ReadyToSchedule: "Ready to schedule",
  Scheduled: "Scheduled",
  Closed: "Closed",
} as const;

export type ReferralStatus = Exclude<keyof typeof referralStatusLabels, "Scheduled">;
export type ReferralStage = Exclude<keyof typeof referralStatusLabels, "MissingInformation">;

export type ReferralAccount = {
  isAdministrator: boolean;
  providerPublicId: string | null;
  memberships: {
    clinicPublicId: string;
    clinicName: string;
    role: "Clinician" | "FrontDesk";
  }[];
};

export type ReferralSummary = {
  publicId: string;
  patientFirstName: string;
  patientLastName: string;
  serviceName: string;
  clinicName: string;
  status: ReferralStatus;
  stage: ReferralStage;
  createdAtUtc: string;
};

export type ReferralDetails = ReferralSummary & {
  patientPublicId: string;
  clinicalServicePublicId: string;
  clinicPublicId: string;
  clinicTimeZoneId: string | null;
  assignedProviderPublicId: string;
  canReview: boolean;
  canReassign: boolean;
  canSchedule: boolean;
  confirmedAppointment: ReferralAppointment | null;
  referringProviderName: string;
  reason: string;
  missingInformationNote: string | null;
};

export type AppointmentSlot = {
  startUtc: string;
  endUtc: string;
};

export type ReferralAppointment = AppointmentSlot & { publicId: string };

export type ReferralAvailability = {
  date: string;
  timeZoneId: string;
  slots: AppointmentSlot[];
};

export type ReferralInput = {
  patientPublicId: string;
  clinicalServicePublicId: string;
  assignedProviderPublicId: string;
  referringProviderName: string;
  reason: string;
};

export class ReferralRequestError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export function referralErrorMessage(error: unknown, fallback: string) {
  if (
    error instanceof ReferralRequestError ||
    error instanceof ClinicRequestError ||
    error instanceof PatientRequestError ||
    error instanceof ProviderRequestError ||
    error instanceof ServiceRequestError
  ) {
    if (error.status === 401) {
      return "Your sign-in couldn’t be verified. Sign out and sign back in.";
    }
    if (error.status === 403) {
      return "Your account doesn’t have permission for this action.";
    }
  }

  if (error instanceof ReferralRequestError && error.message) return error.message;
  return fallback;
}

function referralPath(publicId: string) {
  return "/api/referrals/" + encodeURIComponent(publicId);
}

type ReferralRequestOptions =
  | { signal: AbortSignal }
  | { method: "POST" | "PUT"; body?: object };

async function referralRequest(
  url: string,
  accessToken: string,
  options: ReferralRequestOptions,
): Promise<Response> {
  const headers = new Headers({ Authorization: "Bearer " + accessToken });
  const isWrite = "method" in options;
  if (isWrite && options.body !== undefined) headers.set("Content-Type", "application/json");

  const response = await fetch(url, {
    headers,
    cache: "no-store",
    ...(isWrite
      ? { method: options.method, body: JSON.stringify(options.body) }
      : { signal: options.signal }),
  });

  if (!response.ok) {
    let message = "";
    if ([400, 404, 409].includes(response.status)) {
      const payload: { message?: unknown } | null = await response.json().catch(() => null);
      if (typeof payload?.message === "string") message = payload.message;
    }
    throw new ReferralRequestError(response.status, message);
  }

  return response;
}

async function readReferralResponse<T>(
  url: string,
  accessToken: string,
  options: ReferralRequestOptions,
): Promise<T> {
  const response = await referralRequest(url, accessToken, options);
  return response.json();
}

export function getReferralAccount(accessToken: string, signal: AbortSignal) {
  return readReferralResponse<ReferralAccount>("/api/me", accessToken, { signal });
}

export function getClinicReferrals(accessToken: string, clinicPublicId: string, signal: AbortSignal) {
  const path = "/api/clinics/" + encodeURIComponent(clinicPublicId) + "/referrals";
  return readReferralResponse<ReferralSummary[]>(path, accessToken, { signal });
}

export function getReferrals(accessToken: string, signal: AbortSignal) {
  return readReferralResponse<ReferralSummary[]>("/api/referrals", accessToken, { signal });
}

export function getReferral(
  accessToken: string,
  publicId: string,
  signal: AbortSignal,
  clinicPublicId: string | null,
) {
  const path = clinicPublicId
    ? "/api/clinics/" + encodeURIComponent(clinicPublicId) + "/referrals/" + encodeURIComponent(publicId)
    : referralPath(publicId);
  return readReferralResponse<ReferralDetails>(path, accessToken, { signal });
}

export function getEligibleProviders(accessToken: string, publicId: string, signal: AbortSignal) {
  return readReferralResponse<Provider[]>(referralPath(publicId) + "/eligible-providers", accessToken, { signal });
}

export function getServiceEligibleProviders(accessToken: string, serviceId: string, signal: AbortSignal) {
  return readReferralResponse<Provider[]>(
    "/api/services/" + encodeURIComponent(serviceId) + "/eligible-providers",
    accessToken,
    { signal },
  );
}

export function createReferral(accessToken: string, input: ReferralInput) {
  return readReferralResponse<{ publicId: string }>("/api/referrals", accessToken, {
    method: "POST",
    body: input,
  });
}

export async function assignReferralProvider(
  accessToken: string,
  publicId: string,
  providerPublicId: string,
): Promise<void> {
  await referralRequest(referralPath(publicId) + "/provider", accessToken, {
    method: "PUT",
    body: { providerPublicId },
  });
}

export async function updateReferralStatus(
  accessToken: string,
  publicId: string,
  status: ReferralStatus,
  missingInformationNote: string | null,
): Promise<void> {
  await referralRequest(referralPath(publicId) + "/status", accessToken, {
    method: "PUT",
    body: { status, missingInformationNote },
  });
}

export function getReferralAvailability(
  accessToken: string,
  providerPublicId: string,
  date: string,
  signal: AbortSignal,
) {
  const path = "/api/providers/" + encodeURIComponent(providerPublicId) + "/availability";
  return readReferralResponse<ReferralAvailability>(
    path + "?" + new URLSearchParams({ date }),
    accessToken,
    { signal },
  );
}

export async function bookReferralAppointment(
  accessToken: string,
  referralId: string,
  providerPublicId: string,
  startUtc: string,
): Promise<void> {
  await referralRequest(referralPath(referralId) + "/appointments", accessToken, {
    method: "POST",
    body: { providerPublicId, startUtc },
  });
}

export async function updateReferralAppointment(
  accessToken: string,
  appointmentId: string,
  action: "cancel" | "complete",
): Promise<void> {
  await referralRequest("/api/appointments/" + encodeURIComponent(appointmentId) + "/" + action, accessToken, {
    method: "POST",
  });
}
