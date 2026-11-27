import LoadingStatus from "../components/LoadingStatus";
import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Link } from "react-router";
import { AppointmentRequestError, getMyAppointments, type PatientAppointment } from "../api/appointments";
import { ActionIcon } from "../components/ActionIcon";
import { AppointmentStatusBadge } from "../features/appointments/components/AppointmentStatusBadge";
import AppointmentTime from "../features/appointments/components/AppointmentTime";

function AppointmentCard({ appointment }: { appointment: PatientAppointment }) {
  return (
    <li className="min-w-0">
      <Link className="appointment-card h-full hover:bg-[var(--muted)]" to={"/my-appointments/" + appointment.publicId}
        aria-label={"View appointment: " + appointment.serviceName}
        aria-describedby={`appointment-time-${appointment.publicId} appointment-status-${appointment.publicId}`}>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h3 className="wrap-break-word font-semibold">{appointment.serviceName}</h3>
          <span id={"appointment-status-" + appointment.publicId}><AppointmentStatusBadge status={appointment.status} /></span>
        </div>
        <p className="text-sm" id={"appointment-time-" + appointment.publicId}>
          <AppointmentTime appointment={appointment} />
        </p>
        <p className="flex items-center justify-between gap-2 text-sm text-subtle">
          <span>{appointment.providerName} · {appointment.clinicName}</span>
          <span className="shrink-0"><ActionIcon name="forward" /></span>
        </p>
      </Link>
    </li>
  );
}

function AppointmentGroups({ appointments }: { appointments: PatientAppointment[] }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 15_000);
    return () => window.clearInterval(timer);
  }, []);

  const upcoming: PatientAppointment[] = [];
  const history: PatientAppointment[] = [];
  for (const appointment of appointments) {
    if (appointment.status === "Confirmed" && Date.parse(appointment.startUtc) >= now) {
      upcoming.push(appointment);
    } else {
      history.push(appointment);
    }
  }
  upcoming.sort((first, second) => Date.parse(first.startUtc) - Date.parse(second.startUtc));

  return <>
    <section className="space-y-3" aria-labelledby="upcoming-heading">
      <h2 className="text-lg font-semibold" id="upcoming-heading">Upcoming</h2>
      {upcoming.length ? (
        <ul className="appointment-grid" aria-labelledby="upcoming-heading">
          {upcoming.map(appointment => <AppointmentCard key={appointment.publicId} appointment={appointment} />)}
        </ul>
      ) : <p className="text-sm text-subtle">You have no upcoming appointments.</p>}
    </section>
    {history.length > 0 && (
      <section className="space-y-3" aria-labelledby="history-heading">
        <h2 className="text-lg font-semibold" id="history-heading">History</h2>
        <ul className="appointment-grid" aria-labelledby="history-heading">
          {history.map(appointment => <AppointmentCard key={appointment.publicId} appointment={appointment} />)}
        </ul>
      </section>
    )}
  </>;
}

export default function MyAppointmentsPage({ userId }: { userId?: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    queryKey: ["my-appointments", userId],
    enabled: Boolean(userId),
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getMyAppointments(token, signal);
    },
    retry: false,
  });
  const forbidden = query.error instanceof AppointmentRequestError && query.error.status === 403;

  return (
    <section className="workspace-panel mx-auto w-full max-w-6xl space-y-4" aria-labelledby="my-appointments-heading">
      <div className="page-heading">
        <h1 id="my-appointments-heading" className="page-title">My appointments</h1>
        {userId && !forbidden && (
          <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>
            <ActionIcon name="refresh" />
            {query.isFetching ? "Refreshing…" : "Refresh"}
          </button>
        )}
      </div>
      {!userId ? (
        <p role="alert">Unable to verify your session. Sign in again.</p>
      ) : query.isPending ? (
        <LoadingStatus>Loading your appointments…</LoadingStatus>
      ) : query.isError ? (
        <p role="alert">
          {forbidden
            ? "Your account doesn’t have access to My appointments. Contact your clinic for help."
            : query.error instanceof AppointmentRequestError
              ? query.error.message
              : "Unable to load your appointments. Try refreshing the list."}
        </p>
      ) : query.data.length === 0 ? (
        <p className="text-sm text-subtle">You have no appointments yet.</p>
      ) : (
        <AppointmentGroups appointments={query.data} />
      )}
    </section>
  );
}
