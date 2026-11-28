import LoadingStatus, { Spinner } from "../../../components/LoadingStatus";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { dateInTimeZone } from "../../../utils/clinicDates";
import {
  bookReferralAppointment,
  getReferralBookingAvailability,
  referralErrorMessage,
  ReferralRequestError,
  type AppointmentSlot,
  type ReferralDetails,
} from "../../../api/referrals";
import { ActionIcon } from "../../../components/ActionIcon";
import BookingDatePicker from "../../appointments/components/BookingDatePicker";

function slotLabel(slot: AppointmentSlot, timeZone: string) {
  const format = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });
  return format.formatRange(new Date(slot.startUtc), new Date(slot.endUtc));
}

function appointmentNextStep(referral: ReferralDetails) {
  const stage = referral.status === "MissingInformation" ? referral.status : referral.stage;
  switch (stage) {
    case "PendingReview":
      return "The receiving clinician needs to review this referral before front desk can book an appointment.";
    case "MissingInformation":
      return "The referring team needs to provide the requested information so the receiving clinician can finish reviewing this referral.";
    case "ReadyToSchedule":
      return referral.canSchedule
        ? "Choose an available time with the assigned provider."
        : "Front desk can now book an appointment with the assigned provider.";
    case "Scheduled":
      return "The receiving clinic has booked an appointment for this referral.";
    case "Closed":
      return "This referral is closed. Contact the referring team if additional care is needed.";
  }
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
  const formId = useId();
  const bookingForm = useRef<HTMLFormElement>(null);
  const appointmentLink = useRef<HTMLAnchorElement>(null);
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const [observedTime, setObservedTime] = useState(() => new Date());
  const [bookingOpen, setBookingOpen] = useState(false);
  const [date, setDate] = useState("");
  const [selection, setSelection] = useState<{ startUtc: string; clinicDay: string; providerId: string } | null>(null);
  const [notice, setNotice] = useState("");

  const appointment = referral.confirmedAppointment;
  const appointmentId = appointment?.publicId;
  const canBook = !appointment && referral.canSchedule && referral.stage === "ReadyToSchedule";
  const timeZone = referral.clinicTimeZoneId;
  const providerId = referral.assignedProviderPublicId;
  const today = timeZone ? dateInTimeZone(observedTime, timeZone) : "";
  const availabilityKey = ["provider-availability", userId, providerId];

  useEffect(() => {
    if (bookingOpen) bookingForm.current?.focus();
  }, [bookingOpen]);

  useEffect(() => {
    if (appointmentId && notice) appointmentLink.current?.focus();
  }, [appointmentId, notice]);

  useEffect(() => {
    if (!bookingOpen || !canBook || !timeZone) return;
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
  }, [bookingOpen, canBook, timeZone]);

  const availability = useQuery({
    queryKey: [...availabilityKey, "referred", referral.publicId, timeZone, today],
    enabled: bookingOpen && canBook && !!timeZone,
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getReferralBookingAvailability(accessToken, referral.publicId, providerId, signal);
    },
    refetchInterval: bookingOpen ? 30_000 : false,
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
      await refreshSchedule();
      setBookingOpen(false);
      setDate("");
      setSelection(null);
      setNotice("Appointment booked.");
    },
    onError: async (error) => {
      if (error instanceof ReferralRequestError && [400, 404, 409].includes(error.status)) {
        setSelection(null);
        await refreshSchedule();
      }
    },
  });

  const busy = referralWritePending || change.isPending;
  const slots = availability.data?.days.find(day => day.date === date && day.status === "Available")?.slots ?? [];
  const selectedSlot = selection?.clinicDay === today && selection.providerId === providerId
    ? slots.find(slot => slot.startUtc === selection.startUtc)
    : undefined;

  function toggleBooking() {
    setBookingOpen(!bookingOpen);
    setObservedTime(new Date());
    setDate("");
    setSelection(null);
    setNotice("");
    change.reset();
  }

  function book(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !bookingOpen || !canBook || !timeZone || !availability.isSuccess || !selectedSlot) return;

    const now = new Date();
    if (dateInTimeZone(now, timeZone) !== today) {
      setObservedTime(now);
      return;
    }
    setNotice("");
    change.mutate(selectedSlot.startUtc);
  }

  return (
    <section aria-labelledby={headingId} className="grid gap-3 rounded-lg border border-panel-border p-4">
      <div>
        <div className="page-heading">
          <h3 id={headingId} className="font-semibold">
            {appointment ? "Scheduled appointment" : canBook ? "Book an appointment" : "Appointment"}
          </h3>
          {appointment && (
            <Link ref={appointmentLink} className="workspace-button" to={"/appointments/" + appointment.publicId}
              state={{ fromReferral: true }}>
              <ActionIcon name="eye" />View appointment
            </Link>
          )}
          {canBook && timeZone && (
            <button type="button" data-variant={bookingOpen ? "secondary" : undefined} aria-expanded={bookingOpen}
              aria-controls={formId} onClick={toggleBooking} disabled={busy}>
              <ActionIcon name={bookingOpen ? "close" : "calendarCheck"} />
              {bookingOpen ? "Cancel booking" : "Book appointment"}
            </button>
          )}
        </div>
        {!appointment && <p className="mt-3 text-sm text-subtle">{appointmentNextStep(referral)}</p>}
      </div>
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
        <form id={formId} ref={bookingForm} tabIndex={-1} aria-label="Choose appointment date and time"
          hidden={!bookingOpen} className="space-y-3" onSubmit={book}>
          <fieldset className="space-y-3" disabled={busy || !bookingOpen}>
            <p className="text-sm text-subtle">Times are shown in the receiving clinic’s time zone ({timeZone}).</p>
            {availability.isPending && <LoadingStatus>Checking available dates…</LoadingStatus>}
            {availability.isError && (
              <div className="space-y-2">
                <p role="alert">{referralErrorMessage(availability.error, "Unable to load available times.")}</p>
                <button type="button" data-variant="secondary" onClick={() => void availability.refetch()}>
                  <ActionIcon name="refresh" />
                  Retry available times
                </button>
              </div>
            )}
            {bookingOpen && availability.isSuccess && (
              <BookingDatePicker label="Appointment date" days={availability.data.days} date={date} onSelect={nextDate => {
                setDate(nextDate);
                setSelection(null);
                setNotice("");
                change.reset();
              }} />
            )}
            {availability.isSuccess && date && (
              slots.length ? (
                <label className="block text-sm font-medium">
                  Available time
                  <select
                    className="mt-1 block rounded-md border border-field-border px-3 py-2"
                    value={selectedSlot?.startUtc ?? ""}
                    required
                    onChange={(event) => {
                      setSelection({ startUtc: event.target.value, clinicDay: today, providerId });
                      change.reset();
                    }}
                  >
                    <option value="">Choose a time</option>
                    {slots.map((slot) => (
                      <option key={slot.startUtc} value={slot.startUtc}>
                        {slotLabel(slot, availability.data.timeZoneId)}
                      </option>
                    ))}
                  </select>
                </label>
              ) : <p>No open times on this date. Choose another day.</p>
            )}
            <button type="submit" disabled={!selectedSlot || !availability.isSuccess}>
              {change.isPending ? <Spinner /> : <ActionIcon name="calendarCheck" />}
              {change.isPending ? "Booking…" : "Confirm booking"}
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
