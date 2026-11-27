import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { AppointmentRequestError, getMyAppointment } from "../api/appointments";
import { ActionIcon } from "../components/ActionIcon";
import LoadingStatus from "../components/LoadingStatus";
import PatientDetailsPanel from "../components/PatientDetailsPanel";
import { AppointmentStatusBadge } from "../features/appointments/components/AppointmentStatusBadge";
import AppointmentTime from "../features/appointments/components/AppointmentTime";

export default function MyAppointmentDetailsPage({ userId }: { userId?: string }) {
  const { publicId = "" } = useParams();
  const { getAccessTokenSilently } = useAuth0();
  const details = useQuery({
    queryKey: ["my-appointments", userId, publicId],
    enabled: Boolean(userId && publicId),
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getMyAppointment(token, publicId, signal);
    },
    retry: false,
  });
  const appointment = details.isSuccess ? details.data : undefined;

  return (
    <PatientDetailsPanel title="Appointment details" backTo="/my-appointments" backLabel="Back to my appointments">
      {!userId ? (
        <p role="alert">Unable to verify your session. Sign in again.</p>
      ) : details.isPending ? (
        <LoadingStatus>Loading your appointment…</LoadingStatus>
      ) : details.isError ? (
        <div className="space-y-2">
          <p role="alert">
            {details.error instanceof AppointmentRequestError
              ? details.error.message
              : "Unable to load your appointment."}
          </p>
          <button type="button" data-variant="secondary" disabled={details.isFetching} onClick={() => void details.refetch()}>
            <ActionIcon name="refresh" />
            {details.isFetching ? "Retrying…" : "Try again"}
          </button>
        </div>
      ) : appointment && (
        <>
          <dl className="grid gap-4 rounded-lg border border-panel-border p-4 sm:grid-cols-2">
            <div><dt className="font-medium">Service</dt><dd>{appointment.serviceName}</dd></div>
            <div><dt className="font-medium">Status</dt><dd><AppointmentStatusBadge status={appointment.status} /></dd></div>
            <div><dt className="font-medium">Clinic</dt><dd>{appointment.clinicName}<br />{appointment.clinicAddress}</dd></div>
            <div><dt className="font-medium">Provider</dt><dd>{appointment.providerName}</dd></div>
            <div className="sm:col-span-2">
              <dt className="font-medium">Appointment time</dt><dd><AppointmentTime appointment={appointment} /></dd>
            </div>
          </dl>
          <p className="text-sm text-subtle">
            {appointment.status === "Confirmed"
              ? "Contact the clinic if you need to reschedule or cancel this appointment."
              : "Contact the clinic if you have questions about this appointment."}
          </p>
          {appointment.referralPublicId && (
            <Link className="workspace-button" data-variant="secondary" to={"/my-referrals/" + appointment.referralPublicId}>
              <ActionIcon name="eye" />
              View referral
            </Link>
          )}
        </>
      )}
    </PatientDetailsPanel>
  );
}
