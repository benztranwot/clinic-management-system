import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { useEffect, useId, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate } from "react-router";
import { AppointmentRequestError, createDirectAppointment, getBookingOptions, searchBookingPatients, type BookingOption } from "../api/appointments";
import type { PatientSummary } from "../api/patients";
import { getReferralAvailability } from "../api/referrals";
import { ActionIcon } from "../components/ActionIcon";
import WorkspacePagination from "../components/WorkspacePagination";
import { addCalendarDays, dateInTimeZone } from "../features/appointments/clinicDates";
import { refreshAppointmentViews } from "../features/appointments/appointmentQueries";

const inputClass = "w-full min-w-0 rounded-md border border-panel-border px-3 py-2";

function PatientPicker({ userId, selected, onSelect, disabled }: {
  userId: string; selected: PatientSummary | null; onSelect: (patient: PatientSummary | null) => void; disabled: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const patientSelectId = useId();
  const [search, setSearch] = useState({ firstName: "", lastName: "", page: 1 });
  const patients = useQuery({
    queryKey: ["patients", userId, "booking", search], retry: false,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session.");
      return searchBookingPatients(token, search, signal);
    },
  });
  function find(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const fields = new FormData(event.currentTarget);
    setSearch({ firstName: String(fields.get("firstName") ?? "").trim(), lastName: String(fields.get("lastName") ?? "").trim(), page: 1 });
    onSelect(null);
  }
  return <section className="space-y-3" aria-labelledby="booking-patient-heading">
    <h2 id="booking-patient-heading" className="font-semibold">Find a patient</h2>
    <form onSubmit={find}>
      <fieldset className="grid items-end gap-3 sm:grid-cols-2" disabled={disabled}>
        <label className="grid gap-1 text-sm">First name<input name="firstName" type="search" maxLength={100} className={inputClass} /></label>
        <label className="grid gap-1 text-sm">Last name<input name="lastName" type="search" maxLength={100} className={inputClass} /></label>
        <button type="submit" data-variant="secondary">Find patients</button>
      </fieldset>
    </form>
    {patients.isPending ? <LoadingStatus>Loading patients...</LoadingStatus> : patients.isError ? <div className="space-y-2">
      <p role="alert">{patients.error.message}</p>
      <button type="button" data-variant="secondary" onClick={() => void patients.refetch()}>Try again</button>
    </div> : <>
      <div className="grid gap-1 text-sm font-medium">
        <label htmlFor={patientSelectId}>Patient</label>
        <select id={patientSelectId} className={inputClass} value={selected?.publicId ?? ""} disabled={disabled || patients.isFetching}
          onChange={event => onSelect(patients.data.items.find(patient => patient.publicId === event.target.value) ?? null)}>
          <option value="">Choose a patient</option>
          {patients.data.items.map(patient => <option key={patient.publicId} value={patient.publicId}>
            {patient.lastName}, {patient.firstName} - {patient.dateOfBirth}
          </option>)}
        </select>
      </div>
      {patients.data.items.length === 0 && <p>No patients match this search.</p>}
      <WorkspacePagination itemCount={patients.data.items.length} page={search.page} pageSize={patients.data.pageSize} count={patients.data.totalCount}
        hasMore={patients.data.hasMore} busy={patients.isFetching || disabled} label="Patient lookup pages"
        onPage={page => { setSearch({ ...search, page }); onSelect(null); }} />
    </>}
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
  const validDate = date >= addCalendarDays(today, 1) && date <= addCalendarDays(today, 30);
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
    queryKey: ["provider-availability", userId, option.providerPublicId, "direct-booking", date, today],
    enabled: validDate, retry: false,
    queryFn: async ({ signal }) => getReferralAvailability(await getToken(), option.providerPublicId, date, signal),
  });
  const slots = availability.data?.slots ?? [];
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });
  const canBook = Boolean(patient && validDate && availability.isSuccess && !availability.isFetching
    && slots.some(slot => slot.startUtc === startUtc));
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
  return <form className="space-y-3" onSubmit={event => { event.preventDefault(); if (canBook && !booking.isPending) booking.mutate(); }}>
    <fieldset className="space-y-3" disabled={booking.isPending}>
      <legend className="mb-3 font-semibold">Choose an appointment time</legend>
      <p className="text-sm text-subtle">Times use the clinic's time zone ({timeZone}). Book from tomorrow through 30 days ahead.</p>
      <label className="grid gap-1 text-sm font-medium">Appointment date
        <input className={inputClass} type="date" required value={date} min={addCalendarDays(today, 1)} max={addCalendarDays(today, 30)}
          onChange={event => { setDate(event.target.value); setStartUtc(""); }} />
      </label>
      {date && !validDate && <p role="alert">Choose a date from tomorrow through 30 days ahead.</p>}
      {validDate && availability.isPending && <LoadingStatus>Loading available times...</LoadingStatus>}
      {availability.isError && <p role="alert">{availability.error.message}</p>}
      {validDate && availability.isSuccess && <>
        <div className="grid gap-1 text-sm font-medium">
          <label htmlFor={slotSelectId}>Appointment time</label>
          <select id={slotSelectId} className={inputClass} required value={startUtc} disabled={availability.isFetching}
            onChange={event => setStartUtc(event.target.value)}>
            <option value="">Choose an available time</option>
            {slots.map(slot => <option key={slot.startUtc} value={slot.startUtc}>
              {time.formatRange(new Date(slot.startUtc), new Date(slot.endUtc))}
            </option>)}
          </select>
        </div>
        {slots.length === 0 && <p>No available times on this date. Choose another date.</p>}
      </>}
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
  return <>
    <div className="page-heading">
      <h1 id="new-appointment-heading" className="page-title">Create appointment</h1>
      <Link className="workspace-button" data-variant="secondary" to={{ pathname: "/appointments/upcoming", search: returnState?.search }} state={returnState}>
        <ActionIcon name="back" />Back to upcoming appointments
      </Link>
    </div>
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
  </>;
}

export default function NewAppointmentPage() {
  const { user } = useAuth0();
  return <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="new-appointment-heading">
    {user?.sub ? <NewAppointment key={user.sub} userId={user.sub} /> : <p role="alert">Unable to verify your session.</p>}
  </section>;
}
