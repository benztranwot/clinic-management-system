import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { useEffect, useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getProviderCalendar,
  markDayUnavailable,
  removeDayOff,
  type CalendarAppointment,
  type CalendarDay,
  type CalendarSlot,
} from "../api/providerSchedule";
import { Link, useLocation, useSearchParams } from "react-router";
import { ActionIcon } from "../components/ActionIcon";
import { AppointmentStatusBadge, TimeOffConflictBadge } from "../features/appointments/components/AppointmentStatusBadge";
import { dateInTimeZone } from "../utils/clinicDates";
import "./ProviderSchedulePage.css";

const weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const dayPeriods = [
  { id: "morning", label: "Morning" },
  { id: "afternoon", label: "Afternoon" },
] as const;
type DayPeriod = (typeof dayPeriods)[number]["id"];

const fullDate = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone: "UTC" });
const monthName = new Intl.DateTimeFormat(undefined, { month: "long", year: "numeric", timeZone: "UTC" });

function monthKey(date: string) {
  return date.slice(0, 7);
}

function calendarDate(date: string) {
  return new Date(date + "T00:00:00Z");
}

function isWeekday(date: string) {
  const day = calendarDate(date).getUTCDay();
  return day >= 1 && day <= 5;
}

function isSelectableDay(day: CalendarDay) {
  return isWeekday(day.date) || day.dayOff || (day.timeOff?.length ?? 0) > 0 || day.appointments.length > 0;
}

function selectableDayInMonth(days: CalendarDay[], date: string) {
  const selectableDays = days.filter(day => monthKey(day.date) === monthKey(date) && isSelectableDay(day));
  return selectableDays.find(day => day.date >= date) ?? selectableDays.at(-1);
}

function dayLabel(day: CalendarDay, today: string, now: number) {
  if (day.dayOff) return "Unavailable";
  if (!isWeekday(day.date)) return "Weekend";
  if (day.date < today) return "Past date";
  const openTimes = day.availableSlots.filter((slot) => Date.parse(slot.startUtc) > now).length;
  if (openTimes === 0) return "No open times";
  return openTimes === 1 ? "1 open time" : openTimes + " open times";
}

type DaySlot = CalendarSlot & {
  appointment?: CalendarAppointment;
  hasEnded?: boolean;
};

function splitDaySlots(day: CalendarDay, timeZone: string) {
  const hour = new Intl.DateTimeFormat("en-US", { hour: "numeric", hourCycle: "h23", timeZone });
  const slots: DaySlot[] = [
    ...day.availableSlots,
    ...(day.startedSlots ?? []),
    ...day.appointments.map((appointment) => ({
      startUtc: appointment.startUtc,
      endUtc: appointment.endUtc,
      appointment,
    })),
  ];
  slots.sort((first, second) => Date.parse(first.startUtc) - Date.parse(second.startUtc));

  const morning: DaySlot[] = [];
  const afternoon: DaySlot[] = [];
  for (const slot of slots) {
    const column = Number(hour.format(new Date(slot.startUtc))) < 12 ? morning : afternoon;
    column.push(slot);
  }

  return { morning, afternoon };
}

function DaySlotGroup({ period, title, active, slots, timeZone, now, returnState }: {
  period: DayPeriod;
  title: string;
  active: boolean;
  slots: DaySlot[];
  timeZone: string;
  now: number;
  returnState: { fromProviderSchedule: true; scheduleDate: string; scheduleOwner: string };
}) {
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  return (
    <section id={"schedule-" + period} aria-labelledby={"schedule-" + period + "-heading"}
      data-selected={active} className="provider-schedule-period min-w-0 space-y-2">
      <h3 id={"schedule-" + period + "-heading"} className="text-sm font-semibold">{title}</h3>
      <ul className="space-y-1">
        {slots.map(({ appointment, startUtc, endUtc, hasEnded }) => {
          const ended = hasEnded === true || Date.parse(endUtc) <= now;
          const started = hasEnded !== undefined || Date.parse(startUtc) <= now;
          const style = ended && !appointment ? " bg-[var(--muted)] text-subtle" : "";
          const range = time.formatRange(new Date(startUtc), new Date(endUtc));
          const description = appointment
            ? appointment.patientName + " · " + appointment.serviceName
            : ended ? "Time passed" : started ? "Time started" : "Open time";
          const content = (
            <>
              <div className="flex min-w-0 items-baseline justify-between gap-2">
                <time dateTime={startUtc} className="min-w-0 truncate font-medium" title={range}>{range}</time>
                {appointment && <span className="shrink-0"><AppointmentStatusBadge status={appointment.status} /></span>}
              </div>
              <div className="flex min-w-0 items-center justify-between gap-2">
                <span className="min-w-0 truncate text-xs" title={appointment ? description : undefined}>{description}</span>
                {appointment?.status === "Confirmed" && appointment.duringTimeOff
                  ? <span className="shrink-0"><TimeOffConflictBadge /></span>
                  : appointment && <ActionIcon name="forward" />}
              </div>
            </>
          );

          return (
            <li key={appointment?.publicId ?? startUtc} className={"min-w-0 rounded-md border border-panel-border text-sm" + style}>
              {appointment ? (
                <Link className="provider-schedule-slot provider-schedule-slot-link"
                  to={"/appointments/" + appointment.publicId}
                  state={{ ...returnState, schedulePeriod: period }}>
                  <span className="sr-only">View appointment: </span>
                  {content}
                </Link>
              ) : (
                <div className="provider-schedule-slot">{content}</div>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function ProviderCalendarView({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const { state } = useLocation();
  const [search, setSearch] = useSearchParams();
  const queryClient = useQueryClient();
  const queryKey = ["provider-calendar", userId];
  const scheduleState = state?.scheduleOwner === userId ? state : undefined;
  const selectedDate = search.get("date") ??
    (typeof scheduleState?.scheduleDate === "string" ? scheduleState.scheduleDate : "");
  const selectedPeriod = search.has("date") ? search.get("period") : scheduleState?.schedulePeriod;
  const [notice, setNotice] = useState("");
  const [now, setNow] = useState(() => Date.now());

  async function getToken() {
    const token = await getAccessTokenSilently();
    if (!token) throw new Error("Unable to verify your session. Sign in again.");
    return token;
  }

  const calendar = useQuery({
    queryKey,
    retry: false,
    queryFn: async ({ signal }) => getProviderCalendar(await getToken(), signal),
  });
  const clinicToday = calendar.data?.today;
  const clinicTimeZone = calendar.data?.timeZoneId;
  const { isFetching, refetch } = calendar;

  // Keep slot styling current and reload server date rules when the clinic day changes.
  useEffect(() => {
    function updateClock() {
      const currentTime = Date.now();
      setNow(currentTime);
      if (!clinicToday || !clinicTimeZone || isFetching) return;

      const currentDate = dateInTimeZone(new Date(currentTime), clinicTimeZone);
      if (currentDate !== clinicToday) void refetch();
    }

    const timer = window.setInterval(updateClock, 15_000);
    window.addEventListener("focus", updateClock);
    document.addEventListener("visibilitychange", updateClock);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("focus", updateClock);
      document.removeEventListener("visibilitychange", updateClock);
    };
  }, [clinicToday, clinicTimeZone, isFetching, refetch]);

  const change = useMutation({
    mutationFn: async (day: CalendarDay) => {
      const token = await getToken();
      if (day.dayOffPublicId) await removeDayOff(token, day.dayOffPublicId);
      else await markDayUnavailable(token, day.date);
    },
    onSuccess: async (_, day) => {
      setNotice(day.dayOffPublicId ? "Day off removed." : "Day marked unavailable.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey }),
        queryClient.invalidateQueries({ queryKey: ["provider-availability", userId] }),
        queryClient.invalidateQueries({ queryKey: ["appointment-attention", userId] }),
        queryClient.invalidateQueries({ queryKey: ["upcoming-appointments", userId] }),
        queryClient.invalidateQueries({ queryKey: ["appointment", userId] }),
      ]);
    },
  });

  if (calendar.isPending) return <LoadingStatus>Loading your calendar…</LoadingStatus>;
  if (calendar.isError) return (
    <div className="space-y-3">
      <p role="alert">{calendar.error.message}</p>
      <button type="button" data-variant="secondary" disabled={calendar.isFetching} onClick={() => void calendar.refetch()}>
        <ActionIcon name="refresh" />
        Try again
      </button>
    </div>
  );

  const { days, timeZoneId, today } = calendar.data;
  const selectedDay = selectableDayInMonth(days, selectedDate || today)
    ?? selectableDayInMonth(days, today)
    ?? days.find(isSelectableDay);
  if (!selectedDay) return <p>No calendar days are available.</p>;
  const selected = selectedDay;

  const month = monthKey(selected.date);
  const months = [...new Set(days.map((day) => monthKey(day.date)))];
  const monthIndex = months.indexOf(month);
  const visibleDays = days.filter((day) => monthKey(day.date) === month);
  const firstVisibleDate = calendarDate(visibleDays[0].date);
  // Reserve columns for the days before the start of the visible range.
  const leadingCells = firstVisibleDate.getUTCDay();
  const trailingCells = (7 - (leadingCells + visibleDays.length) % 7) % 7;
  const slotsByPeriod = splitDaySlots(selected, timeZoneId);
  const periods = dayPeriods.filter(({ id }) => slotsByPeriod[id].length > 0);
  const activePeriod = periods.find(({ id }) => id === selectedPeriod)?.id ?? periods[0]?.id;
  const appointmentCount = selected.appointments.length;
  const timeOff = selected.timeOff ?? [];
  const isPast = selected.date < today;
  const canChangeDay = !isPast && (selected.canMarkUnavailable || selected.dayOffPublicId);
  const selectedDateLabel = fullDate.format(calendarDate(selected.date));
  const returnState = { fromProviderSchedule: true, scheduleDate: selected.date, scheduleOwner: userId } as const;
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone: timeZoneId });

  function selectDay(date: string) {
    if (date !== selected.date) setSearch({ date }, { replace: true });
    setNotice("");
    change.reset();
  }

  function moveMonth(offset: number) {
    const target = months[monthIndex + offset];
    const firstDay = selectableDayInMonth(days, target === monthKey(today) ? today : target + "-01");
    if (firstDay) selectDay(firstDay.date);
  }

  function updateDay() {
    if (change.isPending || !canChangeDay) return;
    if (!selected.dayOffPublicId && selected.appointments.length > 0 &&
      !window.confirm("Mark this day unavailable? Existing appointments stay booked.")) return;
    setNotice("");
    change.mutate(selected);
  }

  return (
    <>
      <p className="text-sm text-subtle">Clinic time zone: {timeZoneId}. Booking opens tomorrow and extends 30 days ahead.</p>
      <div className="provider-schedule-layout">
        <section className="provider-schedule-calendar min-w-0 space-y-3" aria-labelledby="schedule-month-heading">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 id="schedule-month-heading" className="text-xl font-semibold" aria-live="polite">{monthName.format(firstVisibleDate)}</h2>
            <div className="flex flex-wrap gap-2">
              <button type="button" data-variant="secondary" disabled={change.isPending || monthIndex === 0} onClick={() => moveMonth(-1)}>
                <ActionIcon name="back" />
                Previous month
              </button>
              <button type="button" data-variant="secondary" disabled={change.isPending || monthIndex === months.length - 1} onClick={() => moveMonth(1)}>
                <ActionIcon name="forward" />
                Next month
              </button>
            </div>
          </div>

          <div role="group" aria-label="Calendar dates" className="grid grid-cols-7 rounded-md border border-panel-border">
            {weekdays.map((day) => <div key={day} className="py-2 text-center text-xs font-medium text-subtle">{day}</div>)}
            {Array.from({ length: leadingCells }, (_, index) => (
              <div key={index} className="provider-calendar-cell border-t border-panel-border" aria-hidden="true" />
            ))}
            {visibleDays.map((day) => (
              <div key={day.date} className="provider-calendar-cell min-w-0 border-t border-panel-border">
                <button
                  type="button"
                  data-variant={day.date === selected.date ? undefined : "secondary"}
                  className="provider-calendar-day"
                  aria-pressed={day.date === selected.date}
                  aria-current={day.date === today ? "date" : undefined}
                  aria-label={fullDate.format(calendarDate(day.date)) + ", " + dayLabel(day, today, now) + ", appointments: " + day.appointments.length}
                  disabled={change.isPending || !isSelectableDay(day)}
                  onClick={() => selectDay(day.date)}
                >
                  <time dateTime={day.date}>{calendarDate(day.date).getUTCDate()}</time>
                  <span className="hidden text-xs sm:block">{dayLabel(day, today, now).replace(/ times?$/, "")}</span>
                  {day.appointments.length > 0 && (
                    <span className="text-xs">{day.appointments.length} {day.appointments.length === 1 ? "appt" : "appts"}</span>
                  )}
                </button>
              </div>
            ))}
            {Array.from({ length: trailingCells }, (_, index) => (
              <div key={index + leadingCells} className="provider-calendar-cell border-t border-panel-border" aria-hidden="true" />
            ))}
          </div>
          {canChangeDay && (
            <div className="flex flex-wrap items-center justify-between gap-3">
              <time className="text-sm font-medium" dateTime={selected.date}>{selectedDateLabel}</time>
              <button
                type="button"
                data-variant={selected.dayOffPublicId ? "secondary" : "danger"}
                disabled={change.isPending}
                onClick={updateDay}
              >
                {change.isPending ? <Spinner /> : <ActionIcon name={selected.dayOffPublicId ? "calendarCheck" : "calendarX"} />}
                {change.isPending ? "Saving…" : selected.dayOffPublicId ? "Remove day off" : "Mark day unavailable"}
              </button>
            </div>
          )}
          {change.isError && <p role="alert">{change.error.message}</p>}
          {notice && <p role="status">{notice}</p>}
        </section>

        <section className="provider-schedule-agenda min-w-0 gap-3" aria-labelledby="schedule-day-heading">
          <div className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
            <h2 id="schedule-day-heading" className="font-semibold">{selectedDateLabel}</h2>
            <div className="flex flex-wrap items-baseline gap-x-2 text-sm text-subtle">
              <p>{dayLabel(selected, today, now)}</p>
              <span aria-hidden="true">·</span>
              <p>
                {appointmentCount === 0
                  ? (isPast ? "No appointments recorded." : "No appointments booked.")
                  : appointmentCount + (appointmentCount === 1 ? " appointment" : " appointments")}
              </p>
            </div>
          </div>
          {selected.dayOff ? <p className="text-sm text-subtle">Time off: All day</p>
            : timeOff.length > 0 && (
              <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-sm text-subtle">
                <p>Time off:</p>
                <ul aria-label="Time off" className="flex flex-wrap gap-x-3 gap-y-1">
                  {timeOff.map(({ startUtc, endUtc }, index) => (
                    <li key={index}>{time.formatRange(new Date(startUtc), new Date(endUtc))}</li>
                  ))}
                </ul>
              </div>
            )}
          {periods.length === 2 && (
            <div className="provider-schedule-periods flex flex-wrap gap-2" role="group" aria-label="Time of day">
              {periods.map(({ id, label }) => (
                <button key={id} type="button" aria-pressed={activePeriod === id} aria-controls={"schedule-" + id}
                  data-variant={activePeriod === id ? undefined : "secondary"}
                  onClick={() => setSearch({ date: selected.date, period: id }, { replace: true })}>
                  {label}
                </button>
              ))}
            </div>
          )}
          {periods.length > 0 && (
            <div className="provider-schedule-slots" role="region" aria-label="Appointment times">
              {periods.map(({ id, label }) => (
                <DaySlotGroup key={id} period={id} title={label} active={activePeriod === id}
                  slots={slotsByPeriod[id]} timeZone={timeZoneId} now={now} returnState={returnState} />
              ))}
            </div>
          )}
        </section>
      </div>
    </>
  );
}

export default function ProviderSchedulePage() {
  const { user } = useAuth0();
  return (
    <section className="workspace-panel provider-schedule-panel mx-auto w-full space-y-4" aria-labelledby="schedule-heading">
      <h1 id="schedule-heading" className="page-title">My schedule</h1>
      {user?.sub
        ? <ProviderCalendarView key={user.sub} userId={user.sub} />
        : <p role="alert">We couldn’t identify your session.</p>}
    </section>
  );
}
