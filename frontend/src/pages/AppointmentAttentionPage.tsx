import { useAuth0 } from "@auth0/auth0-react";
import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { Link } from "react-router";
import { getUpcomingAppointments, type AppointmentSummary } from "../api/appointments";
import CancelAppointmentButton from "../components/appointments/CancelAppointmentButton";
import { appointmentLists, type AppointmentListView } from "../components/appointments/appointmentLists";
import { useAppointmentAttention } from "../components/appointments/useAppointmentAttention";

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
  userId, view, query,
}: {
  userId: string;
  view: AppointmentListView;
  query: UseQueryResult<AppointmentSummary[]>;
}) {
  const { title, empty } = appointmentLists[view];

  return (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="appointment-list-heading">
      <div className="page-heading">
        <h1 id="appointment-list-heading" className="page-title">{title}</h1>
        <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>
          {query.isFetching ? "Refreshing…" : "Refresh"}
        </button>
      </div>
      {query.isPending ? (
        <p role="status">Loading appointments…</p>
      ) : query.isError ? (
        <p role="alert">{query.error.message}</p>
      ) : query.data.length === 0 ? (
        <p className="text-sm text-subtle">{empty}</p>
      ) : (
        <ul className="space-y-3" aria-labelledby="appointment-list-heading">
          {query.data.map((appointment) => (
            <AppointmentCard key={appointment.publicId} appointment={appointment} userId={userId} view={view} />
          ))}
        </ul>
      )}
    </section>
  );
}

function AvailabilityConflicts({ userId }: { userId: string }) {
  const query = useAppointmentAttention(userId);
  return <AppointmentList userId={userId} view="conflicts" query={query} />;
}

function UpcomingAppointments({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    queryKey: ["upcoming-appointments", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getUpcomingAppointments(token, signal);
    },
    retry: false,
  });
  return <AppointmentList userId={userId} view="upcoming" query={query} />;
}

export default function AppointmentListPage({ view }: { view: AppointmentListView }) {
  const { user } = useAuth0();
  const userId = user?.sub;
  if (!userId) return <p role="alert">We couldn’t identify your session.</p>;

  return view === "conflicts" ? <AvailabilityConflicts userId={userId} /> : <UpcomingAppointments userId={userId} />;
}
