import LoadingStatus from "../components/LoadingStatus";
import WorkspacePagination from "../components/WorkspacePagination";
import { readPage } from "../components/useListSearch";
import { useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import WorkspaceFilterDialog from "../components/WorkspaceFilterDialog";
import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useLocation, useSearchParams } from "react-router";
import { searchUpcomingAppointments, type AppointmentSummary, type UpcomingAppointmentSearch } from "../api/appointments";
import { AppointmentStatusBadge, TimeOffConflictBadge } from "../features/appointments/components/AppointmentStatusBadge";
import { appointmentLists, appointmentPageSize, type AppointmentListView } from "../features/appointments/appointmentLists";
import { useReferralAccount } from "../features/referrals/hooks/useReferralAccount";
import { ActionIcon } from "../components/ActionIcon";

function AppointmentCard({
  appointment, userId, view, returnSearch, patientFilter,
}: {
  appointment: AppointmentSummary;
  userId: string;
  view: AppointmentListView;
  returnSearch?: string;
  patientFilter?: string;
}) {
  const statusId = useId();
  const timeZone = appointment.timeZoneId ?? "UTC";
  const start = new Date(appointment.startUtc);
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });
  const when = `${date.format(start)} · ${time.formatRange(start, new Date(appointment.endUtc))}`;

  return (
    <li className="min-w-0">
      <Link className="appointment-card h-full hover:bg-[var(--muted)]"
        aria-label={`View appointment: ${appointment.patientName} · ${appointment.serviceName} · ${when} (${timeZone})`}
        aria-describedby={statusId}
        to={{ pathname: "/appointments/" + appointment.publicId, search: returnSearch }}
        state={{ appointmentList: view, patientFilter, filterOwner: userId }}>
        <div className="flex flex-wrap items-start justify-between gap-x-2 gap-y-1">
          <p className="wrap-break-word font-semibold">{appointment.patientName}
            <span className="ml-2 text-sm font-normal text-subtle">{appointment.serviceName}</span>
          </p>
          <div id={statusId} className="flex flex-wrap gap-1">
            <AppointmentStatusBadge status="Confirmed" />
            {view === "conflicts" && <TimeOffConflictBadge>Overlaps provider time off</TimeOffConflictBadge>}
          </div>
        </div>
        <p className="text-sm">
          <time dateTime={appointment.startUtc} className="font-medium">{when}</time>
          {" "}({timeZone})
        </p>
        <p className="flex items-center justify-between gap-2 text-sm text-subtle">
          <span>{appointment.providerName} · {appointment.clinicName}</span>
          <span className="shrink-0"><ActionIcon name="forward" /></span>
        </p>
      </Link>
    </li>
  );
}

function AppointmentList({
  userId, view, appointments, error, retry, controls, pagination, returnSearch, patientFilter, onClearFilters,
}: {
  userId: string;
  view: AppointmentListView;
  appointments?: AppointmentSummary[];
  error: Error | null;
  retry: () => void;
  controls?: ReactNode;
  pagination?: ReactNode;
  returnSearch?: string;
  patientFilter?: string;
  onClearFilters?: () => void;
}) {
  const { title, empty } = appointmentLists[view];

  return (
    <section className="workspace-panel mx-auto w-full max-w-6xl space-y-4" aria-labelledby="appointment-list-heading">
      <div className="page-heading">
        <h1 id="appointment-list-heading" className="page-title">{title}</h1>
        <div className="flex flex-wrap gap-2">
          {controls}
        </div>
      </div>
      {onClearFilters && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-subtle">Filters active</span>
          <button type="button" data-variant="secondary" onClick={onClearFilters}>
            <ActionIcon name="reset" />Clear filters
          </button>
        </div>
      )}
      {error ? (
        <div className="space-y-2"><p role="alert">{error.message}</p><button type="button" data-variant="secondary" onClick={retry}>Try again</button></div>
      ) : !appointments ? (
        <LoadingStatus>Loading appointments…</LoadingStatus>
      ) : appointments.length === 0 ? (
        <p className="text-sm text-subtle" role="status">
          {onClearFilters ? "No appointments match these filters." : empty}
        </p>
      ) : (
        <ul className="appointment-grid" aria-labelledby="appointment-list-heading">
          {appointments.map((appointment) => (
            <AppointmentCard key={appointment.publicId} appointment={appointment} userId={userId} view={view}
              returnSearch={returnSearch} patientFilter={patientFilter} />
          ))}
        </ul>
      )}
      {pagination}
    </section>
  );
}

const nameFields = [
  ["clinic", "Clinic"], ["service", "Service"], ["provider", "Provider"], ["patient", "Patient"],
] as const;
const filterTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

function dateParameter(value: string | null) {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith("0000")) return "";
  const date = new Date(value + "T00:00:00Z");
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value ? value : "";
}

function localMidnight(date: string, nextDay = false) {
  const midnight = new Date(date + "T00:00:00");
  if (nextDay) midnight.setDate(midnight.getDate() + 1);
  return midnight.toISOString();
}

function SchedulingAppointments({ userId, view }: { userId: string; view: AppointmentListView }) {
  const { getAccessTokenSilently } = useAuth0();
  const account = useReferralAccount(userId).data?.account;
  const canSchedule = account?.isAdministrator || account?.memberships.some(membership => membership.role === "FrontDesk");
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const from = dateParameter(params.get("from"));
  const end = dateParameter(params.get("through"));
  const through = end === "9999-12-31" || (from && end < from) ? "" : end;
  // Keep patient search text out of URLs and referrer headers.
  const patientFilter = location.state?.filterOwner === userId && typeof location.state.patientFilter === "string"
    ? location.state.patientFilter
    : "";
  const search: UpcomingAppointmentSearch = {
    clinic: (params.get("clinic") ?? "").trim().slice(0, 200),
    service: (params.get("service") ?? "").trim().slice(0, 200),
    provider: (params.get("provider") ?? "").trim().slice(0, 200),
    patient: patientFilter,
    fromUtc: from ? localMidnight(from) : undefined,
    untilUtc: through ? localMidnight(through, true) : undefined,
    latestFirst: params.get("sort") === "latest",
    page: readPage(params.get("page"), appointmentPageSize),
  };
  const hasFilters = Boolean(search.clinic || search.service || search.provider || patientFilter || from || through);
  const [dateError, setDateError] = useState("");
  const filtersDialog = useRef<HTMLDialogElement>(null);
  const query = useQuery({
    queryKey: [view === "conflicts" ? "appointment-attention" : "upcoming-appointments", userId, search],
    refetchInterval: 30_000,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return searchUpcomingAppointments(token, search, signal, view);
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
    const next = new URLSearchParams();
    for (const name of ["clinic", "service", "provider", "from", "through", "sort"]) {
      const field = value(name);
      if (field && !(name === "sort" && field === "earliest")) next.set(name, field);
    }
    setParams(next, { state: { patientFilter: value("patient"), filterOwner: userId } });
    filtersDialog.current?.close();
  }

  function goToPage(page: number) {
    const next = new URLSearchParams(params);
    if (page === 1) next.delete("page");
    else next.set("page", String(page));
    setParams(next, { state: { patientFilter, filterOwner: userId } });
  }

  function clearFilters() {
    setDateError("");
    setParams({}, { state: null });
  }

  return (
    <AppointmentList
      userId={userId} view={view} appointments={query.data?.items}
      returnSearch={location.search} patientFilter={patientFilter}
      error={query.error} retry={() => void query.refetch()}
      onClearFilters={hasFilters ? clearFilters : undefined}
      controls={<>
        <WorkspaceFilterDialog dialogRef={filtersDialog} title={"Filter " + appointmentLists[view].title.toLowerCase()}>
          <form key={location.key} className="space-y-4"
            aria-label={"Filter " + appointmentLists[view].title.toLowerCase()} onSubmit={applyFilters}
            onReset={clearFilters}>
            <div className="grid gap-3 sm:grid-cols-2">
              {nameFields.map(([name, label]) => (
                <label key={name} className="grid gap-1 text-sm font-medium">
                  {label}
                  <input name={name} type="search" maxLength={200} defaultValue={search[name]}
                    className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
                </label>
              ))}
              <label className="grid gap-1 text-sm font-medium">
                Start date
                <input name="from" type="date" defaultValue={from} aria-describedby="appointment-date-zone"
                  className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
              </label>
              <label className="grid gap-1 text-sm font-medium">
                End date
                <input name="through" type="date" max="9999-12-30" defaultValue={through} aria-describedby="appointment-date-zone"
                  className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
              </label>
              <label className="grid gap-1 text-sm font-medium">
                Sort by time
                <select name="sort" defaultValue={search.latestFirst ? "latest" : "earliest"} className="min-w-0 rounded-md border border-panel-border px-3 py-2">
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
        {view === "upcoming" && canSchedule && <Link to="/appointments/new" className="workspace-button"
          state={{ appointmentReturn: { search: location.search, patientFilter, filterOwner: userId } }}>
          <ActionIcon name="plus" />Create appointment
        </Link>}
      </>}
      pagination={<WorkspacePagination itemCount={query.isSuccess ? query.data.items.length : 0} page={search.page} pageSize={query.data?.pageSize ?? appointmentPageSize} count={query.data?.totalCount}
        hasMore={query.data?.hasMore ?? false} busy={query.isFetching}
        onPage={goToPage} label={appointmentLists[view].title + " pages"} />}

    />
  );
}

export default function AppointmentListPage({ view }: { view: AppointmentListView }) {
  const { user } = useAuth0();
  const userId = user?.sub;
  if (!userId) return <p role="alert">We couldn’t identify your session.</p>;

  return <SchedulingAppointments key={userId + view} userId={userId} view={view} />;
}
