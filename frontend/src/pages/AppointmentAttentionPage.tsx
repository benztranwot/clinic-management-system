import { useRef, useState, type FormEvent, type ReactNode } from "react";
import WorkspaceFilterDialog from "../components/WorkspaceFilterDialog";
import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { searchUpcomingAppointments, type AppointmentSummary, type UpcomingAppointmentSearch } from "../api/appointments";
import CancelAppointmentButton from "../components/appointments/CancelAppointmentButton";
import { appointmentLists, type AppointmentListView } from "../components/appointments/appointmentLists";
import { useAppointmentAttention } from "../components/appointments/useAppointmentAttention";
import { ActionIcon } from "../components/ActionIcon";

function AppointmentCard({
  appointment, userId, view,
}: {
  appointment: AppointmentSummary;
  userId: string;
  view: AppointmentListView;
}) {
  const timeZone = appointment.timeZoneId ?? "UTC";
  const start = new Date(appointment.startUtc);
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  return (
    <li className="space-y-3 rounded-lg border border-panel-border p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="wrap-break-word font-semibold">{appointment.patientName}</p>
          <p className="text-sm text-subtle">{appointment.serviceName}</p>
        </div>
        <span className="rounded-md bg-[var(--muted)] px-2 py-1 text-sm font-medium">
          {appointmentLists[view].badge}
        </span>
      </div>
      <p className="text-sm">
        <time dateTime={appointment.startUtc} className="font-medium">
          {date.format(start)} · {time.formatRange(start, new Date(appointment.endUtc))}
        </time>
        {" "}({timeZone})
      </p>
      <p className="text-sm text-subtle">{appointment.providerName} · {appointment.clinicName}</p>
      <div className="flex flex-wrap gap-2">
        <Link className="workspace-button" to={"/appointments/" + appointment.publicId} state={{ appointmentList: view }}>
          <ActionIcon name="eye" />
          View appointment
        </Link>
        {view === "conflicts" && !appointment.referralPublicId && (
          <CancelAppointmentButton publicId={appointment.publicId} userId={userId} canCancel />
        )}
      </div>
    </li>
  );
}

function AppointmentList({
  userId, view, appointments, isFetching, error, refresh, controls, pagination,
}: {
  userId: string;
  view: AppointmentListView;
  appointments?: AppointmentSummary[];
  isFetching: boolean;
  error: Error | null;
  refresh: () => void;
  controls?: ReactNode;
  pagination?: ReactNode;
}) {
  const { title, empty } = appointmentLists[view];

  return (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="appointment-list-heading">
      <div className="page-heading">
        <h1 id="appointment-list-heading" className="page-title">{title}</h1>
        <div className="flex flex-wrap gap-2">
          {controls}
          <button type="button" data-variant="secondary" disabled={isFetching} onClick={refresh}>
            <ActionIcon name="refresh" />
            {isFetching ? "Refreshing…" : "Refresh"}
          </button>
        </div>
      </div>
      {error ? (
        <p role="alert">{error.message}</p>
      ) : !appointments ? (
        <p role="status">Loading appointments…</p>
      ) : appointments.length === 0 ? (
        <p className="text-sm text-subtle">{empty}</p>
      ) : (
        <ul className="space-y-3" aria-labelledby="appointment-list-heading">
          {appointments.map((appointment) => (
            <AppointmentCard key={appointment.publicId} appointment={appointment} userId={userId} view={view} />
          ))}
        </ul>
      )}
      {pagination}
    </section>
  );
}

function AvailabilityConflicts({ userId }: { userId: string }) {
  const query = useAppointmentAttention(userId);
  return <AppointmentList userId={userId} view="conflicts" appointments={query.data}
    isFetching={query.isFetching} error={query.error} refresh={() => void query.refetch()} />;
}

const initialSearch = { clinic: "", service: "", provider: "", patient: "", latestFirst: false, page: 1 };
const nameFields = [
  ["clinic", "Clinic"], ["service", "Service"], ["provider", "Provider"], ["patient", "Patient"],
] as const;
const filterTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

function localMidnight(date: string, nextDay = false) {
  const midnight = new Date(date + "T00:00:00");
  if (nextDay) midnight.setDate(midnight.getDate() + 1);
  return midnight.toISOString();
}

function UpcomingAppointments({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [search, setSearch] = useState<UpcomingAppointmentSearch>(initialSearch);
  const [dateError, setDateError] = useState("");
  const filtersDialog = useRef<HTMLDialogElement>(null);
  const query = useQuery({
    queryKey: ["upcoming-appointments", userId, search],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return searchUpcomingAppointments(token, search, signal);
    },
    retry: false,
  });

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const fields = new FormData(event.currentTarget);
    const value = (name: string) => String(fields.get(name) ?? "").trim();
    const from = value("from");
    const through = value("through");
    if (from && through && from > through) {
      setDateError("End date must be on or after the start date.");
      return;
    }

    setDateError("");
    setSearch({
      clinic: value("clinic"), service: value("service"), provider: value("provider"), patient: value("patient"),
      fromUtc: from ? localMidnight(from) : undefined,
      untilUtc: through ? localMidnight(through, true) : undefined,
      latestFirst: value("sort") === "latest",
      page: 1,
    });
    filtersDialog.current?.close();
  }

  return (
    <AppointmentList
      userId={userId} view="upcoming" appointments={query.data?.items}
      isFetching={query.isFetching} error={query.error} refresh={() => void query.refetch()}
      controls={
        <WorkspaceFilterDialog dialogRef={filtersDialog} title="Filter upcoming appointments">
          <form className="space-y-4"
            aria-label="Filter upcoming appointments" onSubmit={applyFilters}
            onReset={() => { setDateError(""); setSearch(initialSearch); }}>
            <div className="grid gap-3 sm:grid-cols-2">
              {nameFields.map(([name, label]) => (
                <label key={name} className="grid gap-1 text-sm font-medium">
                  {label}
                  <input name={name} type="search" maxLength={200}
                    className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
                </label>
              ))}
              <label className="grid gap-1 text-sm font-medium">
                Start date
                <input name="from" type="date" aria-describedby="appointment-date-zone"
                  className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
              </label>
              <label className="grid gap-1 text-sm font-medium">
                End date
                <input name="through" type="date" aria-describedby="appointment-date-zone"
                  className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
              </label>
              <label className="grid gap-1 text-sm font-medium">
                Sort by time
                <select name="sort" className="min-w-0 rounded-md border border-panel-border px-3 py-2">
                  <option value="earliest">Earliest first</option>
                  <option value="latest">Latest first</option>
                </select>
              </label>
            </div>
            <p id="appointment-date-zone" className="text-sm text-subtle">Date filters use your time zone ({filterTimeZone}).</p>
            {dateError && <p role="alert">{dateError}</p>}
            <div className="flex flex-wrap gap-2">
              <button type="submit"><ActionIcon name="check" />Apply filters</button>
              <button type="reset" data-variant="secondary"><ActionIcon name="reset" />Clear filters</button>
              <button type="button" data-variant="secondary" onClick={() => filtersDialog.current?.close()}><ActionIcon name="close" />Close</button>
            </div>
          </form>
        </WorkspaceFilterDialog>
      }
      pagination={(search.page > 1 || query.data?.hasMore) && (
        <nav className="flex flex-wrap items-center gap-3" aria-label="Upcoming appointment pages">
          <button type="button" data-variant="secondary" disabled={search.page === 1 || query.isFetching}
            onClick={() => setSearch((current) => ({ ...current, page: current.page - 1 }))}><ActionIcon name="back" />Previous</button>
          <span role="status">Page {search.page}</span>
          <button type="button" data-variant="secondary" disabled={!query.data?.hasMore || query.isFetching}
            onClick={() => setSearch((current) => ({ ...current, page: current.page + 1 }))}><ActionIcon name="forward" />Next</button>
        </nav>
      )}
    />
  );
}

export default function AppointmentListPage({ view }: { view: AppointmentListView }) {
  const { user } = useAuth0();
  const userId = user?.sub;
  if (!userId) return <p role="alert">We couldn’t identify your session.</p>;

  return view === "conflicts" ? <AvailabilityConflicts userId={userId} /> : <UpcomingAppointments userId={userId} />;
}
