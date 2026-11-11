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

const weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const fullDate = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone: "UTC" });
const monthName = new Intl.DateTimeFormat(undefined, { month: "long", year: "numeric", timeZone: "UTC" });

function monthKey(date: string) {
  return date.slice(0, 7);
}

function calendarDate(date: string) {
  return new Date(date + "T00:00:00Z");
}

function dayLabel(day: CalendarDay, now: number) {
  if (day.dayOff) return "Unavailable";
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

function DaySlotColumn({ title, slots, timeZone, now }: { title: string; slots: DaySlot[]; timeZone: string; now: number }) {
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  return (
    <div className="min-w-0 space-y-3">
      <h4 className="text-sm font-semibold">{title}</h4>
      {slots.length === 0 ? <p className="text-sm text-subtle">No open times or appointments.</p> : (
        <ul className="space-y-2">
          {slots.map(({ appointment, startUtc, endUtc, hasEnded }) => {
            const ended = hasEnded === true || Date.parse(endUtc) <= now;
            const started = hasEnded !== undefined || Date.parse(startUtc) <= now;
            const style = ended ? " bg-[var(--muted)] text-subtle" : "";

            return (
              <li
                key={appointment?.publicId ?? startUtc}
                className={"space-y-1 rounded-md border border-panel-border p-3 text-sm" + style}
              >
                <time dateTime={startUtc} className="block font-medium">
                  {time.formatRange(new Date(startUtc), new Date(endUtc))}
                </time>
                {appointment ? (
                  <>
                    <p className="wrap-break-word">{appointment.serviceName} · {appointment.patientName}</p>
                    {appointment.duringTimeOff && <p className="text-subtle">Booked during time off</p>}
                  </>
                ) : (
                  <p className="text-subtle">{ended ? "Time passed" : started ? "Time started" : "Open time"}</p>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

function ProviderCalendarView({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const queryKey = ["provider-calendar", userId];
  const [selectedDate, setSelectedDate] = useState("");
  const [notice, setNotice] = useState("");
  const [now, setNow] = useState(() => Date.now());

  // Keep slot styling current without polling the calendar API.
  useEffect(() => {
    function updateClock() {
      setNow(Date.now());
    }

    const timer = window.setInterval(updateClock, 15_000);
    window.addEventListener("focus", updateClock);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("focus", updateClock);
    };
  }, []);

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
      ]);
    },
  });

  if (calendar.isPending) return <p role="status">Loading your calendar…</p>;
  if (calendar.isError) return (
    <div className="space-y-3">
      <p role="alert">{calendar.error.message}</p>
      <button type="button" data-variant="secondary" disabled={calendar.isFetching} onClick={() => void calendar.refetch()}>
        Try again
      </button>
    </div>
  );

  const { days, timeZoneId, today } = calendar.data;
  const selected = days.find((day) => day.date === selectedDate) ?? days[0];
  if (!selected) return <p>No calendar days are available.</p>;

  const month = monthKey(selected.date);
  const months = [...new Set(days.map((day) => monthKey(day.date)))];
  const monthIndex = months.indexOf(month);
  const visibleDays = days.filter((day) => monthKey(day.date) === month);
  const firstVisibleDate = calendarDate(visibleDays[0].date);
  // Reserve columns for the days before the start of the visible range.
  const leadingCells = firstVisibleDate.getUTCDay();
  const { morning, afternoon } = splitDaySlots(selected, timeZoneId);
  const appointmentCount = selected.appointments.length;

  function selectDay(date: string) {
    setSelectedDate(date);
    setNotice("");
    change.reset();
  }

  function moveMonth(offset: number) {
    const target = months[monthIndex + offset];
    const firstDay = days.find((day) => monthKey(day.date) === target);
    if (firstDay) selectDay(firstDay.date);
  }

  function updateDay() {
    if (change.isPending || (!selected.canMarkUnavailable && !selected.dayOffPublicId)) return;
    if (!selected.dayOffPublicId && selected.appointments.length > 0 &&
      !window.confirm("Mark this day unavailable? Existing appointments stay booked.")) return;
    setNotice("");
    change.mutate(selected);
  }

  return (
    <>
      <p className="text-sm text-subtle">Next 90 days in the clinic’s time zone ({timeZoneId}). Booking opens up to 30 days ahead.</p>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-xl font-semibold">{monthName.format(firstVisibleDate)}</h2>
        <div className="flex gap-2">
          <button type="button" data-variant="secondary" disabled={change.isPending || monthIndex === 0} onClick={() => moveMonth(-1)}>
            Previous month
          </button>
          <button type="button" data-variant="secondary" disabled={change.isPending || monthIndex === months.length - 1} onClick={() => moveMonth(1)}>
            Next month
          </button>
        </div>
      </div>

      <div role="region" aria-label="Calendar dates" tabIndex={0} className="overflow-x-auto rounded-md border border-panel-border">
        <div className="grid min-w-[32rem] grid-cols-7">
          {weekdays.map((day) => <div key={day} className="p-2 text-center text-sm font-medium text-subtle">{day}</div>)}
          {Array.from({ length: leadingCells }, (_, index) => (
            <div key={index} className="border-t border-panel-border" aria-hidden="true" />
          ))}
          {visibleDays.map((day) => (
            <div key={day.date} className="min-w-0 border-t border-panel-border p-1">
              <button
                type="button"
                data-variant={day.date === selected.date ? undefined : "secondary"}
                className="w-full text-left"
                aria-pressed={day.date === selected.date}
                aria-current={day.date === today ? "date" : undefined}
                aria-label={fullDate.format(calendarDate(day.date)) + ", " + dayLabel(day, now) + ", appointments: " + day.appointments.length}
                disabled={change.isPending}
                onClick={() => selectDay(day.date)}
              >
                <time dateTime={day.date}>{calendarDate(day.date).getUTCDate()}</time>
                <span className="mt-1 block text-xs">{dayLabel(day, now)}</span>
                {day.appointments.length > 0 && (
                  <span className="mt-1 block text-xs">{day.appointments.length} booked</span>
                )}
              </button>
            </div>
          ))}
        </div>
      </div>

      <section className="grid gap-4 rounded-md border border-panel-border p-4 md:grid-cols-[minmax(0,2fr)_minmax(0,3fr)_minmax(0,3fr)]" aria-labelledby="schedule-day-heading">
        <div className="min-w-0 space-y-3">
          <h3 id="schedule-day-heading" className="font-semibold">{fullDate.format(calendarDate(selected.date))}</h3>
          <p>{dayLabel(selected, now)}</p>
          <p className="text-sm text-subtle">
            {appointmentCount === 0
              ? "No appointments booked."
              : appointmentCount + (appointmentCount === 1 ? " appointment" : " appointments")}
          </p>
          {(selected.canMarkUnavailable || selected.dayOffPublicId) && (
            <button
              type="button"
              data-variant={selected.dayOffPublicId ? "secondary" : undefined}
              disabled={change.isPending}
              onClick={updateDay}
            >
              {change.isPending ? "Saving…" : selected.dayOffPublicId ? "Remove day off" : "Mark day unavailable"}
            </button>
          )}
          {change.isError && <p role="alert">{change.error.message}</p>}
          {notice && <p role="status">{notice}</p>}
        </div>
        <DaySlotColumn title="Morning" slots={morning} timeZone={timeZoneId} now={now} />
        <DaySlotColumn title="Afternoon" slots={afternoon} timeZone={timeZoneId} now={now} />
      </section>
    </>
  );
}

export default function ProviderSchedulePage() {
  const { user } = useAuth0();
  return (
    <section className="workspace-panel mx-auto w-full max-w-5xl space-y-4" aria-labelledby="schedule-heading">
      <h1 id="schedule-heading" className="page-title">My schedule</h1>
      {user?.sub
        ? <ProviderCalendarView key={user.sub} userId={user.sub} />
        : <p role="alert">We couldn’t identify your session.</p>}
    </section>
  );
}
