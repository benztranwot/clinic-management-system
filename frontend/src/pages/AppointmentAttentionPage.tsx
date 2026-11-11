import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { getAppointmentConflicts, type AppointmentConflict } from "../api/appointments";

function ConflictCard({ appointment }: { appointment: AppointmentConflict }) {
  const timeZone = appointment.timeZoneId ?? "UTC";
  const start = new Date(appointment.startUtc);
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  return (
    <li className="space-y-3 rounded-lg border border-panel-border p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 className="wrap-break-word font-semibold">{appointment.patientName}</h2>
          <p className="text-sm text-subtle">{appointment.serviceName}</p>
        </div>
        <span className="rounded-md bg-[var(--muted)] px-2 py-1 text-sm font-medium">Overlaps provider time off</span>
      </div>
      <p className="text-sm">
        <time dateTime={appointment.startUtc} className="font-medium">
          {date.format(start)} · {time.formatRange(start, new Date(appointment.endUtc))}
        </time>
        {" "}({timeZone})
      </p>
      <p className="text-sm text-subtle">{appointment.providerName} · {appointment.clinicName}</p>
      {appointment.referralPublicId && (
        <Link
          className="workspace-button"
          to={{
            pathname: "/referrals/" + appointment.referralPublicId,
            search: new URLSearchParams({ clinic: appointment.clinicPublicId }).toString(),
          }}
        >
          Open referral
        </Link>
      )}
    </li>
  );
}

export default function AppointmentAttentionPage() {
  const { user, getAccessTokenSilently } = useAuth0();
  const userId = user?.sub;
  const queue = useQuery({
    queryKey: ["appointment-attention", userId],
    enabled: Boolean(userId),
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getAppointmentConflicts(token, signal);
    },
    retry: false,
  });

  return (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="attention-heading">
      <div className="page-heading">
        <h1 id="attention-heading" className="page-title">Needs attention</h1>
        {userId && (
          <button type="button" data-variant="secondary" disabled={queue.isFetching} onClick={() => void queue.refetch()}>
            {queue.isFetching ? "Refreshing…" : "Refresh"}
          </button>
        )}
      </div>
      {!userId ? (
        <p role="alert">We couldn’t identify your session.</p>
      ) : queue.isPending ? (
        <p role="status">Loading appointments…</p>
      ) : queue.isError ? (
        <p role="alert">{queue.error.message}</p>
      ) : queue.data.length === 0 ? (
        <p className="text-sm text-subtle">No confirmed appointments overlap provider time off.</p>
      ) : (
        <ul className="space-y-3" aria-label="Appointments that need attention">
          {queue.data.map((appointment) => <ConflictCard key={appointment.publicId} appointment={appointment} />)}
        </ul>
      )}
    </section>
  );
}
