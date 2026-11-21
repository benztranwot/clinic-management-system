import { searchPage } from "./paging";
import type { PatientSummary } from "./patients";
import type { PageResult } from "./paging";
export type AppointmentSummary = {
  publicId: string;
  startUtc: string;
  endUtc: string;
  clinicPublicId: string;
  clinicName: string;
  timeZoneId: string | null;
  patientName: string;
  providerName: string;
  serviceName: string;
  referralPublicId: string | null;
};

export type AppointmentDetails = AppointmentSummary & {
  providerPublicId: string;
  status: "Confirmed" | "Cancelled" | "Completed";
  canCancel: boolean;
  canComplete: boolean;
  canReschedule: boolean;
  overlapsProviderTimeOff: boolean;
};

export type PatientAppointment = Pick<AppointmentSummary,
  "publicId" | "startUtc" | "endUtc" | "clinicName" | "timeZoneId" | "providerName" | "serviceName"
> & Pick<AppointmentDetails, "status">;

export async function getMyAppointments(accessToken: string, signal: AbortSignal): Promise<PatientAppointment[]> {
  const response = await requestAppointment("/api/me/appointments", accessToken,
    "Unable to load your appointments.", { signal });
  return response.json();
}

export class AppointmentRequestError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function requestAppointment(
  path: string,
  accessToken: string,
  fallback: string,
  options: RequestInit,
) {
  const headers = new Headers({ Authorization: "Bearer " + accessToken });
  if (options.body) headers.set("Content-Type", "application/json");
  const response = await fetch(path, { ...options, headers, cache: "no-store" });

  if (response.ok) return response;
  if (response.status === 401) throw new AppointmentRequestError(401, "Your session expired. Sign in again.");
  if (response.status === 404) throw new AppointmentRequestError(404, "Appointment not found.");
  if (response.status === 403) throw new AppointmentRequestError(403, "You don’t have access to this appointment view.");

  const body: { message?: unknown } | null = await response.json().catch(() => null);
  throw new AppointmentRequestError(response.status, typeof body?.message === "string" ? body.message : fallback);
}

export async function getAppointmentConflicts(accessToken: string, signal: AbortSignal): Promise<AppointmentSummary[]> {
  const response = await requestAppointment("/api/appointments/needs-attention", accessToken,
    "Unable to load availability conflicts.", { signal });
  return response.json();
}

export async function getAppointment(publicId: string, accessToken: string, signal: AbortSignal): Promise<AppointmentDetails> {
  const response = await requestAppointment("/api/appointments/" + encodeURIComponent(publicId), accessToken,
    "Unable to load the appointment.", { signal });
  return response.json();
}

export type UpcomingAppointmentSearch = {
  clinic: string;
  service: string;
  provider: string;
  patient: string;
  fromUtc?: string;
  untilUtc?: string;
  latestFirst: boolean;
  page: number;
};

export async function searchUpcomingAppointments(
  accessToken: string,
  search: UpcomingAppointmentSearch,
  signal: AbortSignal,
  view: "upcoming" | "conflicts" = "upcoming",
): Promise<PageResult<AppointmentSummary>> {
  const response = await requestAppointment(`/api/appointments/${view === "conflicts" ? "needs-attention" : "upcoming"}/search`, accessToken,
    view === "conflicts" ? "Unable to load availability conflicts." : "Unable to load upcoming appointments.", { method: "POST", body: JSON.stringify(search), signal });
  return response.json();
}

export async function cancelAppointment(publicId: string, accessToken: string): Promise<void> {
  await requestAppointment("/api/appointments/" + encodeURIComponent(publicId) + "/cancel", accessToken,
    "Unable to cancel the appointment.", { method: "POST" });
}

export async function completeAppointment(publicId: string, accessToken: string): Promise<void> {
  await requestAppointment("/api/appointments/" + encodeURIComponent(publicId) + "/complete", accessToken,
    "Unable to complete the appointment.", { method: "POST" });
}

export async function rescheduleAppointment(
  publicId: string,
  accessToken: string,
  expectedStartUtc: string,
  startUtc: string,
): Promise<void> {
  await requestAppointment("/api/appointments/" + encodeURIComponent(publicId) + "/reschedule", accessToken,
    "Unable to reschedule the appointment.", {
      method: "PUT",
      body: JSON.stringify({ expectedStartUtc, startUtc }),
    });
}

export async function getAppointmentConflictCount(accessToken: string, signal: AbortSignal): Promise<{ count: number }> {
  const response = await requestAppointment("/api/appointments/needs-attention/count", accessToken,
    "Unable to load availability conflicts.", { signal });
  return response.json();
}

export type BookingOption = {
  clinicPublicId: string; clinicName: string; timeZoneId: string | null;
  clinicalServicePublicId: string; serviceName: string; providerPublicId: string; providerName: string;
};

export async function getBookingOptions(token: string, signal: AbortSignal): Promise<BookingOption[]> {
  const response = await requestAppointment("/api/appointments/booking-options", token,
    "Unable to load direct-booking options.", { signal });
  return response.json();
}

export function searchBookingPatients(token: string, search: { firstName: string; lastName: string; page: number }, signal: AbortSignal) {
  return searchPage<PatientSummary>("/api/patients/search", token, search, signal,
    status => new AppointmentRequestError(status, status === 403 ? "Your account cannot look up patients. Contact an administrator." : "Unable to load patients."));
}

export async function createDirectAppointment(token: string, input: {
  patientPublicId: string; clinicalServicePublicId: string; providerPublicId: string; startUtc: string;
}): Promise<{ publicId: string }> {
  const response = await requestAppointment("/api/appointments", token,
    "Unable to confirm the booking. Check upcoming appointments before trying again.",
    { method: "POST", body: JSON.stringify(input) });
  return response.json();
}
