export type CalendarSlot = {
  startUtc: string;
  endUtc: string;
};

export type StartedCalendarSlot = CalendarSlot & {
  hasEnded: boolean;
};

export type CalendarAppointment = CalendarSlot & {
  publicId: string;
  serviceName: string;
  patientName: string;
  duringTimeOff: boolean;
};

export type CalendarDay = {
  date: string;
  openSlots: number;
  availableSlots: CalendarSlot[];
  startedSlots: StartedCalendarSlot[];
  dayOff: boolean;
  dayOffPublicId: string | null;
  canMarkUnavailable: boolean;
  appointments: CalendarAppointment[];
};

export type ProviderCalendar = {
  timeZoneId: string;
  today: string;
  days: CalendarDay[];
};

async function calendarRequest(path: string, accessToken: string, options: RequestInit) {
  const response = await fetch(path, {
    ...options,
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store",
  });
  if (response.ok) return response;

  if (response.status === 401) throw new Error("Your session expired. Sign in again.");
  if (response.status === 403) throw new Error("My schedule is available to clinicians with a linked provider account.");

  const body: { message?: unknown } | null = await response.json().catch(() => null);
  const fallback = options.method ? "Unable to change this day’s availability." : "Unable to load your schedule.";
  throw new Error(typeof body?.message === "string" && body.message ? body.message : fallback);
}

export async function getProviderCalendar(accessToken: string, signal: AbortSignal): Promise<ProviderCalendar> {
  const response = await calendarRequest("/api/me/calendar", accessToken, { signal });
  return response.json();
}

export async function markDayUnavailable(accessToken: string, date: string) {
  await calendarRequest("/api/me/calendar/" + encodeURIComponent(date) + "/time-off", accessToken, { method: "POST" });
}

export async function removeDayOff(accessToken: string, publicId: string) {
  await calendarRequest("/api/me/time-off/" + encodeURIComponent(publicId), accessToken, { method: "DELETE" });
}
