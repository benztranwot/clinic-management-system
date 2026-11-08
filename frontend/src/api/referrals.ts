import { ClinicRequestError } from "./clinics";
import { PatientRequestError } from "./patients";
import { ProviderRequestError, type Provider } from "./providers";
import { ServiceRequestError } from "./services";

export const referralStatusLabels = {
  PendingReview: "Pending review",
  MissingInformation: "Missing information",
  ReadyToSchedule: "Ready to schedule",
  Closed: "Closed",
} as const;

export type ReferralStatus = keyof typeof referralStatusLabels;

export type ReferralSummary = {
  publicId: string;
  patientFirstName: string;
  patientLastName: string;
  serviceName: string;
  clinicName: string;
  status: ReferralStatus;
  createdAtUtc: string;
};

export type ReferralDetails = ReferralSummary & {
  patientPublicId: string;
  clinicalServicePublicId: string;
  clinicPublicId: string;
  assignedProviderPublicId: string | null;
  referringProviderName: string;
  reason: string;
  missingInformationNote: string | null;
};

export type ReferralInput = {
  patientPublicId: string;
  clinicalServicePublicId: string;
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

async function referralRequest<T>(
  url: string,
  accessToken: string,
  options: { signal: AbortSignal } | { method: "POST" | "PUT"; body: object },
): Promise<T> {
  const headers = new Headers({ Authorization: "Bearer " + accessToken });
  const isWrite = "method" in options;
  if (isWrite) headers.set("Content-Type", "application/json");

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

  return response.json();
}

export function getReferrals(accessToken: string, signal: AbortSignal) {
  return referralRequest<ReferralSummary[]>("/api/referrals", accessToken, { signal });
}

export function getReferral(accessToken: string, publicId: string, signal: AbortSignal) {
  return referralRequest<ReferralDetails>(referralPath(publicId), accessToken, { signal });
}

export function getEligibleProviders(accessToken: string, publicId: string, signal: AbortSignal) {
  return referralRequest<Provider[]>(referralPath(publicId) + "/eligible-providers", accessToken, { signal });
}

export function createReferral(accessToken: string, input: ReferralInput) {
  return referralRequest<{ publicId: string }>("/api/referrals", accessToken, {
    method: "POST",
    body: input,
  });
}

export function assignReferralProvider(accessToken: string, publicId: string, providerPublicId: string) {
  return referralRequest<unknown>(referralPath(publicId) + "/provider", accessToken, {
    method: "PUT",
    body: { providerPublicId },
  });
}

export function updateReferralStatus(
  accessToken: string,
  publicId: string,
  status: ReferralStatus,
  missingInformationNote: string | null,
) {
  return referralRequest<unknown>(referralPath(publicId) + "/status", accessToken, {
    method: "PUT",
    body: { status, missingInformationNote },
  });
}
