import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useInfiniteQuery, useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate } from "react-router";
import { AppointmentRequestError, createDirectAppointment, getBookingOptions, getDirectBookingAvailability, searchBookingPatients, type BookingOption } from "../api/appointments";
import type { PatientSummary } from "../api/patients";
import { ActionIcon } from "../components/ActionIcon";
import BookingDatePicker from "../features/appointments/components/BookingDatePicker";
import { dateInTimeZone } from "../utils/clinicDates";
import { refreshAppointmentViews } from "../features/appointments/appointmentQueries";

const inputClass = "w-full min-w-0 rounded-md border border-panel-border px-3 py-2";

type BookingPatientSearch = { firstName: string; lastName: string; dateOfBirth?: string };

function PatientPicker({ userId, selected, onSelect, disabled }: {
  userId: string; selected: PatientSummary | null; onSelect: (patient: PatientSummary | null) => void; disabled: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const dropdown = useRef<HTMLDetailsElement>(null);
  const summary = useRef<HTMLElement>(null);
  const resultList = useRef<HTMLUListElement>(null);
  const nextPageMarker = useRef<HTMLLIElement>(null);
  const [search, setSearch] = useState<BookingPatientSearch | null>(null);
  const patients = useInfiniteQuery({
    queryKey: ["patients", userId, "booking", search],
    enabled: search !== null,
    initialPageParam: 1,
    retry: false,
    queryFn: async ({ pageParam, signal }) => {
      if (!search) throw new Error("Enter a name or date of birth.");
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session.");
      return searchBookingPatients(token, { ...search, page: pageParam }, signal);
    },
    getNextPageParam: last => last.hasMore ? last.page + 1 : undefined,
  });
  const matches = patients.data?.pages.flatMap(page => page.items) ?? [];
  const selectedLabel = selected && `${selected.firstName} ${selected.lastName} · ${selected.dateOfBirth}`;
  const { fetchNextPage, hasNextPage, isFetching, isFetchNextPageError } = patients;

  useEffect(() => {
    const list = resultList.current;
    const marker = nextPageMarker.current;
    if (!list || !marker || !hasNextPage || isFetching || isFetchNextPageError) return;

    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting) void fetchNextPage({ cancelRefetch: false });
    }, { root: list, rootMargin: "0px 0px 96px 0px" });
    observer.observe(marker);
    return () => observer.disconnect();
  }, [fetchNextPage, hasNextPage, isFetching, isFetchNextPageError, matches.length]);

  function close() {
    if (dropdown.current) dropdown.current.open = false;
    summary.current?.focus();
  }

  function find(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const fields = new FormData(event.currentTarget);
    const firstName = String(fields.get("firstName") ?? "").trim();
    const lastName = String(fields.get("lastName") ?? "").trim();
    const dateOfBirth = String(fields.get("dateOfBirth") ?? "");
    setSearch(firstName || lastName || dateOfBirth ? { firstName, lastName, dateOfBirth: dateOfBirth || undefined } : null);
    onSelect(null);
  }

  return <section className="space-y-3" aria-labelledby="booking-patient-heading">
    <h2 id="booking-patient-heading" className="font-semibold">Find a patient</h2>
    <details ref={dropdown} className="group rounded-md border border-panel-border" onKeyDown={event => {
      if (event.key === "Escape" && dropdown.current?.open) {
        event.preventDefault();
        close();
      }
    }}>
      <summary ref={summary} className="flex cursor-pointer items-center justify-between gap-2 px-3 py-2 text-sm font-medium focus-visible:outline-2 focus-visible:outline-action"
        aria-disabled={disabled} onClick={event => { if (disabled) event.preventDefault(); }}>
        {selectedLabel || "Choose a patient"}
        <span className="shrink-0 group-open:rotate-90"><ActionIcon name="forward" /></span>
      </summary>
      <fieldset className="space-y-3 border-t border-panel-border p-3" disabled={disabled}>
        <form onSubmit={find} className="grid items-end gap-3 sm:grid-cols-2">
          <label className="grid gap-1 text-sm">First name<input name="firstName" type="search" maxLength={100} className={inputClass} /></label>
          <label className="grid gap-1 text-sm">Last name<input name="lastName" type="search" maxLength={100} className={inputClass} /></label>
          <label className="grid gap-1 text-sm">Date of birth<input name="dateOfBirth" type="date" className={inputClass} /></label>
          <button type="submit" data-variant="secondary">Find patients</button>
        </form>
        {!search ? <p className="text-sm text-subtle">Search by name or exact date of birth.</p>
          : patients.isPending ? <LoadingStatus>Loading patients…</LoadingStatus>
          : patients.isError && !patients.data ? <div className="space-y-2">
            <p role="alert">{patients.error.message}</p>
            <button type="button" data-variant="secondary" onClick={() => void patients.refetch()}>Try again</button>
          </div>
          : matches.length === 0 ? <p role="status" className="text-sm text-subtle">No patients match this search.</p>
          : <>
            <ul ref={resultList} aria-label="Matching patients" className="max-h-56 space-y-1 overflow-y-auto p-1">
              {matches.map(patient => <li key={patient.publicId}>
                <button type="button" data-variant="secondary" className="w-full text-left"
                  onClick={() => { onSelect(patient); close(); }}>
                  {patient.firstName} {patient.lastName} · {patient.dateOfBirth}
                </button>
              </li>)}
              {hasNextPage && <li ref={nextPageMarker} aria-hidden="true" className="h-px" />}
            </ul>
            {patients.isFetchingNextPage && <LoadingStatus>Loading more matches…</LoadingStatus>}
            {isFetchNextPageError && <div className="space-y-2">
              <p role="alert">{patients.error.message}</p>
              <button type="button" data-variant="secondary" disabled={isFetching}
                onClick={() => void fetchNextPage({ cancelRefetch: false })}>Retry loading matches</button>
            </div>}
          </>}
      </fieldset>
    </details>
  </section>;
}

function BookingTime({ option, patient, userId, onCreated }: {
  option: BookingOption; patient: PatientSummary | null; userId: string; onCreated: (publicId: string) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const slotSelectId = useId();
  const timeZone = option.timeZoneId!;
  const [today, setToday] = useState(() => dateInTimeZone(new Date(), timeZone));
  const [date, setDate] = useState("");
  const [startUtc, setStartUtc] = useState("");
  useEffect(() => {
    const update = () => setToday(dateInTimeZone(new Date(), timeZone));
    const timer = window.setInterval(update, 15_000);
    window.addEventListener("focus", update);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", update); };
  }, [timeZone]);
  async function getToken() {
    const token = await getAccessTokenSilently();
    if (!token) throw new Error("Unable to verify your session.");
    return token;
  }
  const availability = useQuery({
    queryKey: ["provider-availability", userId, option.providerPublicId, "direct-booking", option.clinicalServicePublicId, today],
    refetchInterval: 30_000, retry: false,
    queryFn: async ({ signal }) => getDirectBookingAvailability(option, await getToken(), signal),
  });
  const displayZone = availability.data?.timeZoneId ?? timeZone;
  const selectedDay = availability.data?.days.find(day => day.date === date);
  const slots = selectedDay?.status === "Available" ? selectedDay.slots : [];
  const hasSelection = slots.some(slot => slot.startUtc === startUtc);
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone: displayZone });
  const canBook = Boolean(patient && availability.isSuccess && !availability.isFetching
    && hasSelection);
  const booking = useMutation({
    mutationKey: ["appointment-create", userId], retry: false,
    mutationFn: async () => {
      if (!patient || !canBook) throw new Error("Choose a patient and an available appointment time.");
      return createDirectAppointment(await getToken(), { patientPublicId: patient.publicId,
        clinicalServicePublicId: option.clinicalServicePublicId, providerPublicId: option.providerPublicId, startUtc });
    },
    onSuccess: async saved => {
      await refreshAppointmentViews(queryClient, userId, saved.publicId);
      onCreated(saved.publicId);
    },
    onError: async error => {
      if (error instanceof AppointmentRequestError && [400, 404, 409].includes(error.status)) {
        setStartUtc("");
        await availability.refetch();
      }
    },
  });
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canBook || booking.isPending) return;
    const currentDay = dateInTimeZone(new Date(), timeZone);
    if (today !== currentDay) {
      setToday(currentDay);
      setStartUtc("");
      return;
    }
    booking.mutate();
  }

  return <form className="space-y-3" onSubmit={submit}>
    <fieldset className="space-y-3" disabled={booking.isPending}>
      <legend className="mb-3 font-semibold">Choose an appointment time</legend>
      <p className="text-sm text-subtle">Times use the clinic's time zone ({displayZone}). Book from tomorrow through 30 days ahead.</p>
      {availability.isPending && <LoadingStatus>Checking available dates…</LoadingStatus>}
      {availability.isError && <div className="space-y-2">
        <p role="alert">{availability.error.message}</p>
        <button type="button" data-variant="secondary" disabled={availability.isFetching}
          onClick={() => void availability.refetch()}>Retry available dates</button>
      </div>}
      {availability.isSuccess && <BookingDatePicker label="Appointment date" days={availability.data.days} date={date}
        onSelect={nextDate => { setDate(nextDate); setStartUtc(""); booking.reset(); }} />}
      {availability.isSuccess && slots.length > 0 && (
        <div className="grid gap-1 text-sm font-medium">
          <label htmlFor={slotSelectId}>Appointment time</label>
          <select id={slotSelectId} className={inputClass} required value={hasSelection ? startUtc : ""} disabled={availability.isFetching}
            onChange={event => { setStartUtc(event.target.value); booking.reset(); }}>
            <option value="">Choose an available time</option>
            {slots.map(slot => <option key={slot.startUtc} value={slot.startUtc}>
              {time.formatRange(new Date(slot.startUtc), new Date(slot.endUtc))}
            </option>)}
          </select>
        </div>
      )}
      {!patient && <p className="text-sm text-subtle">Select a patient before booking.</p>}
      <button type="submit" disabled={!canBook || booking.isPending}>{booking.isPending ? <Spinner /> : <ActionIcon name="calendarCheck" />}
        {booking.isPending ? "Creating appointment..." : "Create appointment"}
      </button>
    </fieldset>
    {booking.isError && <p role="alert">{booking.error.message}</p>}
  </form>;
}

function optionKey(option: BookingOption) {
  return [option.clinicPublicId, option.clinicalServicePublicId, option.providerPublicId].join(":");
}
function validZone(zone: string | null) {
  if (!zone) return false;
  try { new Intl.DateTimeFormat(undefined, { timeZone: zone }); return true; } catch { return false; }
}

function NewAppointment({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const navigate = useNavigate();
  const { state } = useLocation();
  const returnState = state?.appointmentReturn?.filterOwner === userId ? state.appointmentReturn : undefined;
  const optionSelectId = useId();
  const [choice, setChoice] = useState("");
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const writing = useIsMutating({ mutationKey: ["appointment-create", userId] }) > 0;
  const options = useQuery({
    queryKey: ["appointment-booking-options", userId], retry: false,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session.");
      return getBookingOptions(token, signal);
    },
  });
  const selected = options.data?.find(option => optionKey(option) === choice);
  return (
    <div className="mx-auto w-full max-w-4xl space-y-3">
      <Link className="workspace-button" data-variant="secondary" to={{ pathname: "/appointments/upcoming", search: returnState?.search }} state={returnState}>
        <ActionIcon name="back" />Back to upcoming appointments
      </Link>
      <section className="workspace-panel space-y-4" aria-labelledby="new-appointment-heading">
        <h1 id="new-appointment-heading" className="page-title">Create appointment</h1>
        {options.isPending ? <LoadingStatus>Loading booking options...</LoadingStatus> : options.isError ? <div className="space-y-2">
          <p role="alert">{options.error.message}</p><button type="button" onClick={() => void options.refetch()}>Try again</button>
        </div> : options.data.length === 0 ? <p>No direct-booking services are available at your clinics. Services that require a referral must be booked through referral details.</p> : <>
          <div className="grid gap-1 text-sm font-medium">
            <label htmlFor={optionSelectId}>Service, clinic and provider</label>
            <select id={optionSelectId} className={inputClass} value={choice} disabled={writing} onChange={event => setChoice(event.target.value)}>
              <option value="">Choose a service, clinic and provider</option>
              {options.data.map(option => <option key={optionKey(option)} value={optionKey(option)}>
                {option.clinicName} - {option.serviceName} - {option.providerName}
              </option>)}
            </select>
          </div>
          <p className="text-sm text-subtle">
            For a service that requires a referral, open the ready referral from the{" "}
            <Link className="text-[var(--link)] underline" to="/referrals">referral queue</Link> to book.
          </p>
          <div className="grid items-start gap-6 lg:grid-cols-2">
            <PatientPicker userId={userId} selected={patient} onSelect={setPatient} disabled={writing} />
            {selected && (validZone(selected.timeZoneId)
              ? <BookingTime key={choice} option={selected} patient={patient} userId={userId} onCreated={publicId => {
                  void navigate({ pathname: "/appointments/" + publicId, search: returnState?.search },
                    { replace: true, state: { ...returnState, appointmentList: "upcoming" } });
                }} />
              : <p role="alert">Configure this clinic's time zone before booking.</p>)}
          </div>
        </>}
      </section>
    </div>
  );
}

export default function NewAppointmentPage() {
  const { user } = useAuth0();
  return user?.sub ? <NewAppointment key={user.sub} userId={user.sub} /> : (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="new-appointment-heading">
      <h1 id="new-appointment-heading" className="page-title">Create appointment</h1>
      <p role="alert">Unable to verify your session.</p>
    </section>
  );
}
