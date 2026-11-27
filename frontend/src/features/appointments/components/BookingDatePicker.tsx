import { useState } from "react";
import type { BookingDay } from "../../../api/appointments";
import { ActionIcon } from "../../../components/ActionIcon";
import { addCalendarDays } from "../../../utils/clinicDates";

const weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const fullDate = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone: "UTC" });
const monthLabel = new Intl.DateTimeFormat(undefined, { month: "long", year: "numeric", timeZone: "UTC" });
const labels = {
  Available: { full: "Available", short: "" },
  OutsideWorkingHours: { full: "Outside working hours", short: "Closed" },
  DayOff: { full: "Day off", short: "Day off" },
  FullyBooked: { full: "Fully booked", short: "Booked" },
};

export default function BookingDatePicker({ label, days, date, onSelect }: {
  label: string;
  days: BookingDay[];
  date: string;
  onSelect: (date: string) => void;
}) {
  const [visibleMonth, setVisibleMonth] = useState("");
  const months = [...new Set(days.map(day => day.date.slice(0, 7)))];
  if (months.length === 0) return <p className="text-sm text-subtle">No dates are available in the booking window.</p>;
  const selectedMonth = date.slice(0, 7);
  const month = months.includes(visibleMonth) ? visibleMonth
    : months.includes(selectedMonth) ? selectedMonth : months[0];
  const monthIndex = months.indexOf(month);
  const firstDay = month + "-01";
  const firstDate = new Date(firstDay + "T00:00:00Z");
  const lastDate = new Date(firstDate);
  lastDate.setUTCMonth(firstDate.getUTCMonth() + 1, 0);
  const leadingCells = firstDate.getUTCDay();
  const byDate = new Map(days.map(day => [day.date, day]));
  const hasOpenDays = days.some(day => day.status === "Available" && day.slots.length > 0);

  return (
    <section aria-label={label} className="max-w-lg space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold" aria-live="polite">{monthLabel.format(firstDate)}</h3>
        <div className="flex gap-2">
          <button type="button" data-variant="secondary" aria-label="Previous month"
            disabled={monthIndex === 0} onClick={() => setVisibleMonth(months[monthIndex - 1])}>
            <ActionIcon name="back" />
          </button>
          <button type="button" data-variant="secondary" aria-label="Next month"
            disabled={monthIndex === months.length - 1} onClick={() => setVisibleMonth(months[monthIndex + 1])}>
            <ActionIcon name="forward" />
          </button>
        </div>
      </div>
      <div className="grid grid-cols-7 gap-1">
        {weekdays.map(weekday => <span key={weekday} className="text-center text-xs text-subtle">{weekday}</span>)}
        {Array.from({ length: leadingCells }, (_, index) => <span key={index} aria-hidden="true" />)}
        {Array.from({ length: lastDate.getUTCDate() }, (_, index) => {
          const localDate = addCalendarDays(firstDay, index);
          const day = byDate.get(localDate);
          const enabled = day?.status === "Available" && day.slots.length > 0;
          const status = day ? labels[day.status] : { full: "Outside booking window", short: "" };
          const selected = enabled && localDate === date;
          const label = fullDate.format(new Date(localDate + "T00:00:00Z")) + ", " + status.full;
          return (
            <button key={localDate} type="button" className="booking-calendar-day"
              data-variant={selected ? undefined : "secondary"} disabled={!enabled}
              aria-label={label} title={label} aria-pressed={selected}
              onClick={() => onSelect(localDate)}>
              {index + 1}
              {status.short && <span className="text-[0.625rem] font-normal">{status.short}</span>}
            </button>
          );
        })}
      </div>
      {!hasOpenDays && <p role="status" className="text-sm text-subtle">No open times in the booking window.</p>}
    </section>
  );
}
