import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { AppointmentRequestError, getMyAppointments, type PatientAppointment } from "../api/appointments";
import { ActionIcon } from "../components/ActionIcon";

function AppointmentCard({ appointment }: { appointment: PatientAppointment }) {
  const timeZone = appointment.timeZoneId ?? "UTC";
  const start = new Date(appointment.startUtc);
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  return (
    <li className="space-y-2 rounded-lg border border-panel-border p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h2 className="wrap-break-word font-semibold">{appointment.serviceName}</h2>
        <span className="rounded-md bg-[var(--muted)] px-2 py-1 text-sm font-medium">
          {appointment.status === "Confirmed" ? "Scheduled" : appointment.status}
        </span>
      </div>
      <p className="text-sm">
        <time dateTime={appointment.startUtc}>
          {date.format(start)} · {time.formatRange(start, new Date(appointment.endUtc))}
        </time>
        {" "}({timeZone})
      </p>
      <p className="text-sm text-subtle">{appointment.providerName} · {appointment.clinicName}</p>
    </li>
  );
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
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="my-appointments-heading">
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
        <p role="status">Loading your appointments…</p>
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
        <ul className="space-y-3" aria-labelledby="my-appointments-heading">
          {query.data.map((appointment) => <AppointmentCard key={appointment.publicId} appointment={appointment} />)}
        </ul>
      )}
    </section>
  );
}
