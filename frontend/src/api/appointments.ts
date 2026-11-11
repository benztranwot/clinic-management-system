export type AppointmentConflict = {
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

export async function getAppointmentConflicts(accessToken: string, signal: AbortSignal): Promise<AppointmentConflict[]> {
  const response = await fetch("/api/appointments/needs-attention", {
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store",
    signal,
  });

  if (response.status === 401) throw new Error("Your session expired. Sign in again.");
  if (response.status === 403) throw new Error("This queue is available to front-desk staff and administrators.");
  if (!response.ok) throw new Error("Unable to load appointments that need attention.");

  return response.json();
}
