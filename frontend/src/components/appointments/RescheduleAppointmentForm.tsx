import { useEffect, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppointmentRequestError, rescheduleAppointment, type AppointmentDetails } from "../../api/appointments";
import { getReferralAvailability } from "../../api/referrals";
import { appointmentWriteKey, refreshAppointmentViews } from "./appointmentQueries";
import { addCalendarDays, dateInTimeZone } from "./clinicDates";

export default function RescheduleAppointmentForm({
  appointment, userId, timeZone, onClose, onSaved,
}: {
  appointment: AppointmentDetails;
  userId: string;
  timeZone: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const [today, setToday] = useState(() => dateInTimeZone(new Date(), timeZone));
  const [date, setDate] = useState(() => dateInTimeZone(new Date(appointment.startUtc), timeZone));
  const [selectedStart, setSelectedStart] = useState("");
  const lastDay = addCalendarDays(today, 30);
  const validDate = date >= today && date <= lastDay;
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  useEffect(() => {
    function refreshDay() {
      setToday(dateInTimeZone(new Date(), timeZone));
    }
    const timer = window.setInterval(refreshDay, 15_000);
    window.addEventListener("focus", refreshDay);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("focus", refreshDay);
    };
  }, [timeZone]);

  async function getToken() {
    const token = await getAccessTokenSilently();
    if (!token) throw new Error("Unable to verify your session. Sign in again.");
    return token;
  }

  const availability = useQuery({
    queryKey: ["provider-availability", userId, appointment.providerPublicId, "reschedule", date, today],
    enabled: validDate,
    queryFn: async ({ signal }) => getReferralAvailability(await getToken(), appointment.providerPublicId, date, signal),
    retry: false,
  });
  const slots = availability.data?.slots.filter((slot) => slot.startUtc !== appointment.startUtc) ?? [];
  const canSave = validDate && availability.isSuccess && !availability.isFetching
    && slots.some((slot) => slot.startUtc === selectedStart);

  const mutationKey = appointmentWriteKey(userId, appointment.publicId);
  const writing = useIsMutating({ mutationKey }) > 0;
  const change = useMutation({
    mutationKey,
    mutationFn: async () => rescheduleAppointment(
      appointment.publicId, await getToken(), appointment.startUtc, selectedStart,
    ),
    retry: false,
    onSuccess: async () => {
      await refreshAppointmentViews(queryClient, userId, appointment.publicId);
      onSaved();
    },
    onError: async (error) => {
      if (error instanceof AppointmentRequestError && [400, 404, 409].includes(error.status)) {
        setSelectedStart("");
        await refreshAppointmentViews(queryClient, userId, appointment.publicId);
      }
    },
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave || writing) return;
    const currentDay = dateInTimeZone(new Date(), timeZone);
    if (currentDay !== today) {
      setSelectedStart("");
      setToday(currentDay);
      return;
    }
    change.mutate();
  }

  return (
    <form className="space-y-3 border-t border-panel-border pt-4" onSubmit={submit}>
      <h2 className="font-semibold">Reschedule with {appointment.providerName}</h2>
      <p className="text-sm text-subtle">Times are shown in the clinic’s time zone ({timeZone}).</p>
      <fieldset className="space-y-3" disabled={writing}>
        <label className="block text-sm font-medium">
          New appointment date
          <input
            type="date"
            min={today}
            max={lastDay}
            value={date}
            className="mt-1 block rounded-md border border-field-border px-3 py-2"
            required
            onChange={(event) => {
              setDate(event.target.value);
              setSelectedStart("");
              change.reset();
            }}
          />
        </label>
        {date && !validDate && <p role="alert">Choose a date from today through 30 days ahead.</p>}
        {validDate && availability.isPending && <p role="status">Checking available times…</p>}
        {validDate && availability.isError && (
          <div className="space-y-2">
            <p role="alert">{availability.error.message}</p>
            <button type="button" data-variant="secondary" disabled={availability.isFetching} onClick={() => void availability.refetch()}>
              Retry available times
            </button>
          </div>
        )}
        {validDate && availability.isSuccess && (slots.length === 0 ? (
          <p className="text-sm text-subtle">No open times on this date. Choose another day.</p>
        ) : (
          <label className="block text-sm font-medium">
            New appointment time
            <select
              className="mt-1 block rounded-md border border-field-border px-3 py-2"
              value={slots.some((slot) => slot.startUtc === selectedStart) ? selectedStart : ""}
              required
              onChange={(event) => {
                setSelectedStart(event.target.value);
                change.reset();
              }}
            >
              <option value="">Choose a time</option>
              {slots.map((slot) => (
                <option key={slot.startUtc} value={slot.startUtc}>
                  {time.formatRange(new Date(slot.startUtc), new Date(slot.endUtc))}
                </option>
              ))}
            </select>
          </label>
        ))}
        <div className="flex flex-wrap gap-2">
          <button type="submit" disabled={!canSave || writing}>
            {change.isPending ? "Saving…" : "Save new time"}
          </button>
          <button type="button" data-variant="secondary" onClick={onClose}>Close reschedule form</button>
        </div>
      </fieldset>
      {change.isError && <p role="alert">{change.error.message}</p>}
    </form>
  );
}
