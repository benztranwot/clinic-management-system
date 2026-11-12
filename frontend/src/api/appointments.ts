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

export async function getUpcomingAppointments(accessToken: string, signal: AbortSignal): Promise<AppointmentSummary[]> {
  const response = await requestAppointment("/api/appointments/upcoming", accessToken,
    "Unable to load upcoming appointments.", { signal });
  return response.json();
}

export async function cancelAppointment(publicId: string, accessToken: string): Promise<void> {
  await requestAppointment("/api/appointments/" + encodeURIComponent(publicId) + "/cancel", accessToken,
    "Unable to cancel the appointment.", { method: "POST" });
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
