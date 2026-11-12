import { useEffect, useId, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { addCalendarDays, dateInTimeZone } from "../appointments/clinicDates";
import {
  bookReferralAppointment,
  getReferralAvailability,
  referralErrorMessage,
  ReferralRequestError,
  type AppointmentSlot,
  type ReferralDetails,
} from "../../api/referrals";
import { ActionIcon } from "../ActionIcon";

function slotLabel(slot: AppointmentSlot, timeZone: string) {
  const format = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });
  return format.formatRange(new Date(slot.startUtc), new Date(slot.endUtc));
}

function ReferralAppointmentPanel({
  referral,
  userId,
  referralWritePending,
  mutationKey,
}: {
  referral: ReferralDetails;
  userId: string;
  referralWritePending: boolean;
  mutationKey: readonly string[];
}) {
  const headingId = useId();
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const [observedTime, setObservedTime] = useState(() => new Date());
  const [date, setDate] = useState("");
  const [selection, setSelection] = useState<{ startUtc: string; clinicDay: string } | null>(null);
  const [notice, setNotice] = useState("");

  const canBook = referral.canSchedule && referral.stage === "ReadyToSchedule";
  const appointment = referral.confirmedAppointment;
  const timeZone = referral.clinicTimeZoneId;
  const providerId = referral.assignedProviderPublicId;
  const today = timeZone ? dateInTimeZone(observedTime, timeZone) : "";
  const firstDay = today ? addCalendarDays(today, 1) : "";
  const lastDay = today ? addCalendarDays(today, 30) : "";
  const dateInRange = !!firstDay && date >= firstDay && date <= lastDay;
  const availabilityKey = ["provider-availability", userId, providerId];

  useEffect(() => {
    if (!canBook || !timeZone) return;
    const clinicTimeZone = timeZone;

    function refreshClinicDay() {
      const now = new Date();
      const currentDay = dateInTimeZone(now, clinicTimeZone);
      setObservedTime((previous) => dateInTimeZone(previous, clinicTimeZone) === currentDay ? previous : now);
    }

    // Focus and visibility events catch up after the browser pauses background timers.
    const timer = window.setInterval(refreshClinicDay, 15_000);
    window.addEventListener("focus", refreshClinicDay);
    document.addEventListener("visibilitychange", refreshClinicDay);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("focus", refreshClinicDay);
      document.removeEventListener("visibilitychange", refreshClinicDay);
    };
  }, [canBook, timeZone]);

  const availability = useQuery({
    queryKey: [...availabilityKey, date, today],
    enabled: canBook && !!timeZone && dateInRange,
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getReferralAvailability(accessToken, providerId, date, signal);
    },
    retry: false,
  });

  async function refreshSchedule() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["referral", userId, referral.publicId] }),
      queryClient.invalidateQueries({ queryKey: ["referrals", userId] }),
      queryClient.invalidateQueries({ queryKey: availabilityKey }),
      queryClient.invalidateQueries({ queryKey: ["appointment-attention", userId] }),
      queryClient.invalidateQueries({ queryKey: ["upcoming-appointments", userId] }),
      queryClient.invalidateQueries({ queryKey: ["provider-calendar", userId] }),
    ]);
  }

  const change = useMutation({
    mutationKey,
    mutationFn: async (startUtc: string) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      // Echo the provider whose availability was shown so a stale assignment cannot be booked.
      await bookReferralAppointment(accessToken, referral.publicId, providerId, startUtc);
    },
    retry: false,
    onSuccess: async () => {
      setSelection(null);
      setNotice("Appointment booked.");
      await refreshSchedule();
    },
    onError: async (error) => {
      if (error instanceof ReferralRequestError && [400, 404, 409].includes(error.status)) {
        setSelection(null);
        await refreshSchedule();
      }
    },
  });

  const busy = referralWritePending || change.isPending;
  const selectedSlot = dateInRange && selection?.clinicDay === today
    ? availability.data?.slots.find((slot) => slot.startUtc === selection.startUtc)
    : undefined;

  function book(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !canBook || !timeZone || !dateInRange || !availability.isSuccess || !selectedSlot) return;

    const now = new Date();
    if (dateInTimeZone(now, timeZone) !== today) {
      setObservedTime(now);
      return;
    }
    setNotice("");
    change.mutate(selectedSlot.startUtc);
  }

  if (!canBook && !appointment && !notice && !change.isError) return null;

  return (
    <section aria-labelledby={headingId} className="space-y-3 border-t border-panel-border pt-4">
      <h3 id={headingId} className="font-semibold">
        {appointment ? "Scheduled appointment" : canBook ? "Book an appointment" : "Appointment"}
      </h3>
      {appointment && (
        <p>
          <time dateTime={appointment.startUtc}>
            {new Intl.DateTimeFormat(undefined, {
              dateStyle: "full", timeZone: timeZone ?? "UTC",
            }).format(new Date(appointment.startUtc))}
          </time>
          {" · "}{slotLabel(appointment, timeZone ?? "UTC")} ({timeZone ?? "UTC"})
        </p>
      )}
      {canBook && !timeZone && <p role="alert">Set the receiving clinic’s time zone before booking.</p>}
      {canBook && timeZone && (
        <form className="space-y-3" onSubmit={book}>
          <fieldset className="space-y-3" disabled={busy}>
            <label className="block text-sm font-medium">
              Appointment date
              <input
                type="date"
                className="mt-1 block rounded-md border border-field-border px-3 py-2"
                value={date}
                min={firstDay}
                max={lastDay}
                required
                onChange={(event) => {
                  setDate(event.target.value);
                  setSelection(null);
                  setNotice("");
                  change.reset();
                }}
              />
            </label>
            <p className="text-sm text-subtle">Times are shown in the receiving clinic’s time zone ({timeZone}).</p>
            {date && !dateInRange && <p role="alert">Choose a date from tomorrow through 30 days ahead in the clinic’s time zone.</p>}
            {dateInRange && availability.isPending && <p role="status">Checking available times…</p>}
            {dateInRange && availability.isError && (
              <div className="space-y-2">
                <p role="alert">{referralErrorMessage(availability.error, "Unable to load available times.")}</p>
                <button type="button" data-variant="secondary" onClick={() => void availability.refetch()}>
                  <ActionIcon name="refresh" />
                  Retry available times
                </button>
              </div>
            )}
            {dateInRange && availability.isSuccess && (
              availability.data.slots.length ? (
                <label className="block text-sm font-medium">
                  Available time
                  <select
                    className="mt-1 block rounded-md border border-field-border px-3 py-2"
                    value={selectedSlot?.startUtc ?? ""}
                    required
                    onChange={(event) => {
                      setSelection({ startUtc: event.target.value, clinicDay: today });
                      change.reset();
                    }}
                  >
                    <option value="">Choose a time</option>
                    {availability.data.slots.map((slot) => (
                      <option key={slot.startUtc} value={slot.startUtc}>
                        {slotLabel(slot, availability.data.timeZoneId)}
                      </option>
                    ))}
                  </select>
                </label>
              ) : <p>No open times on this date. Choose another day.</p>
            )}
            <button type="submit" disabled={!dateInRange || !selectedSlot || !availability.isSuccess}>
              <ActionIcon name="calendarCheck" />
              {change.isPending ? "Booking…" : "Book appointment"}
            </button>
          </fieldset>
        </form>
      )}
      {change.isError && (
        <p role="alert">{referralErrorMessage(change.error, "Unable to confirm the appointment change. Refresh this referral before trying again.")}</p>
      )}
      {notice && <p role="status">{notice}</p>}
    </section>
  );
}

export default ReferralAppointmentPanel;
